import React, { useState, useEffect, useCallback, useMemo } from 'react';
import { Segmented, Spin, Typography, Empty, message } from 'antd';
import dayjs from 'dayjs';
import utc from 'dayjs/plugin/utc';
import {
  LineChart,
  Line,
  CartesianGrid,
  XAxis,
  YAxis,
  Tooltip as RechartsTooltip,
  ResponsiveContainer,
} from 'recharts';
import { getStockHistory, getEurUsdRate } from '../services/api';
import type { StockHistoryPoint, StockHistoryRange } from '../types';

dayjs.extend(utc);

const { Text } = Typography;

// For 24h/today views treat large market-closure gaps as line breaks.
const SHORT_INTRADAY_GAP_THRESHOLD_MS = 2 * 60 * 60 * 1000;
// Minimal positive offset so Recharts treats the inserted null point as a distinct timestamp.
const MIN_GAP_MARKER_OFFSET_MS = 1;
// For 1w, gaps are compressed by using an index-based X axis; only 24h/today need gap markers.
const historyGapThresholdMsByRange: Partial<Record<StockHistoryRange, number>> = {
  '24h': SHORT_INTRADAY_GAP_THRESHOLD_MS,
  today: SHORT_INTRADAY_GAP_THRESHOLD_MS,
};

const formatSigned = (value: number, suffix = '') =>
  `${value >= 0 ? '+' : ''}${value.toFixed(2)}${suffix}`;

const COLOR_POSITIVE = '#389e0d';
const COLOR_NEGATIVE = '#cf1322';
const COLOR_PRIMARY = '#1677ff';
const COLOR_SECONDARY_TEXT = '#8c8c8c';

const xAxisFormatByRange: Record<StockHistoryRange, string> = {
  '5y': 'MM.YYYY',
  '3y': 'MM.YYYY',
  '1y': 'DD.MM.YY',
  '6m': 'DD.MM.YY',
  '3m': 'DD.MM.YY',
  '1m': 'DD',
  '1w': 'DD.MM HH:mm',
  '24h': 'HH:mm',
  today: 'HH:mm',
};

type HistoryChartPoint = {
  timestamp: string;
  timestampMs: number;
  closeChart: number | null;
  /** Sequential position used as the X-axis coordinate for the 1w compressed view. */
  chartIndex?: number;
};

export interface StockPriceChartProps {
  /** DOM id for the chart panel container (used by aria-controls). */
  panelId?: string;
  stockId: number;
  ticker: string;
  name: string;
  /** Live price in EUR (from live price fetch). Used for period change display. */
  livePriceEur?: number | null;
  /** Live price in USD (from live price fetch). Used for USD-mode current price display. */
  livePriceUsd?: number | null;
  /** Stored / fallback current price in EUR (used when no live price is available). */
  storedPriceEur?: number | null;
}

const StockPriceChart: React.FC<StockPriceChartProps> = ({
  panelId,
  stockId,
  ticker,
  name,
  livePriceEur,
  livePriceUsd,
  storedPriceEur,
}) => {
  const [historyRange, setHistoryRange] = useState<StockHistoryRange>('1y');
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyData, setHistoryData] = useState<StockHistoryPoint[]>([]);
  const [historyEurUsdRate, setHistoryEurUsdRate] = useState<number | null>(null);

  useEffect(() => {
    const fetchHistory = async () => {
      setHistoryLoading(true);
      try {
        const res = await getStockHistory(stockId, historyRange);
        setHistoryData(res.data);
        try {
          const eurUsdRes = await getEurUsdRate();
          setHistoryEurUsdRate(eurUsdRes.data.eurUsd);
        } catch {
          setHistoryEurUsdRate(null);
          message.warning('Не удалось получить курс EUR/USD. История отображается в USD.');
        }
      } catch {
        setHistoryData([]);
        setHistoryEurUsdRate(null);
        message.error('Ошибка загрузки исторических данных');
      } finally {
        setHistoryLoading(false);
      }
    };

    fetchHistory();
  }, [stockId, historyRange]);

  const historyHasEurConversion = historyEurUsdRate != null && historyEurUsdRate > 0;
  const historyCurrencyCode = historyHasEurConversion ? 'EUR' : 'USD';
  const historyCurrencySymbol = historyHasEurConversion ? '€' : '$';
  const convertedHistoryRate = historyHasEurConversion ? historyEurUsdRate : null;

  const historyChartData = useMemo<HistoryChartPoint[]>(() => {
    const sortedPoints: HistoryChartPoint[] = historyData
      .map((point) => ({
        timestamp: point.timestamp,
        timestampMs: dayjs.utc(point.timestamp).valueOf(),
        closeChart: convertedHistoryRate ? point.close / convertedHistoryRate : point.close,
      }))
      .sort((left, right) => left.timestampMs - right.timestampMs);

    // For 1w, assign a sequential index so every observation occupies equal horizontal space
    // and overnight / weekend / holiday closures do not leave empty gaps on the X axis.
    if (historyRange === '1w') {
      return sortedPoints.map((pt, idx) => ({ ...pt, chartIndex: idx }));
    }

    const gapThresholdMs = historyGapThresholdMsByRange[historyRange];
    if (!gapThresholdMs || sortedPoints.length < 2) {
      return sortedPoints;
    }

    const pointsWithGaps: HistoryChartPoint[] = [sortedPoints[0]];
    let previousPoint = sortedPoints[0];
    for (let i = 1; i < sortedPoints.length; i += 1) {
      const currentPoint = sortedPoints[i];
      const gapMs = currentPoint.timestampMs - previousPoint.timestampMs;
      if (gapMs > gapThresholdMs) {
        // Keep marker just after the last real point so Recharts registers a distinct null marker and breaks the line.
        const gapTimestampMs = previousPoint.timestampMs + MIN_GAP_MARKER_OFFSET_MS;
        pointsWithGaps.push({
          timestamp: dayjs(gapTimestampMs).toISOString(),
          timestampMs: gapTimestampMs,
          closeChart: null,
        });
      }
      pointsWithGaps.push(currentPoint);
      previousPoint = currentPoint;
    }

    return pointsWithGaps;
  }, [historyData, convertedHistoryRate, historyRange]);

  /** Maps chartIndex → timestampMs for the 1w compressed view (used by tick and tooltip formatters). */
  const weeklyIndexToTimestampMs = useMemo(() => {
    const map = new Map<number, number>();
    if (historyRange === '1w') {
      historyChartData.forEach((pt) => {
        if (pt.chartIndex !== undefined) {
          map.set(pt.chartIndex, pt.timestampMs);
        }
      });
    }
    return map;
  }, [historyRange, historyChartData]);

  /** Resolves the real timestampMs for a given 1w chart index. */
  const resolveWeeklyTs = useCallback(
    (idx: number) => weeklyIndexToTimestampMs.get(Math.round(idx)),
    [weeklyIndexToTimestampMs],
  );

  // Current price in EUR: prefer live, fall back to stored
  const currentPriceEur = livePriceEur ?? storedPriceEur ?? null;

  const periodStartPriceEur = useMemo(() => {
    if (!historyHasEurConversion) return null;
    for (const point of historyChartData) {
      if (point.closeChart != null) return point.closeChart;
    }
    return null;
  }, [historyChartData, historyHasEurConversion]);

  const periodChangeEur =
    periodStartPriceEur != null && currentPriceEur != null
      ? currentPriceEur - periodStartPriceEur
      : null;
  const periodChangePercent =
    periodChangeEur != null && periodStartPriceEur != null && periodStartPriceEur !== 0
      ? (periodChangeEur / periodStartPriceEur) * 100
      : null;
  const performanceColor =
    periodChangeEur == null
      ? undefined
      : periodChangeEur >= 0
        ? COLOR_POSITIVE
        : COLOR_NEGATIVE;

  // Display current price in the same currency as the chart (EUR when conversion available, USD otherwise)
  const currentPriceDisplay: number | null = historyHasEurConversion
    ? currentPriceEur
    : (livePriceUsd ?? null);

  return (
    <div
      id={panelId}
      style={{
        padding: '16px 20px',
        background: '#f0f7ff',
        borderBottom: '3px solid #1677ff',
      }}
    >
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          marginBottom: 12,
          flexWrap: 'wrap',
          gap: 8,
        }}
      >
        <Text strong style={{ fontSize: 15 }}>
          История цены: {ticker} — {name}
        </Text>
        <Segmented
          value={historyRange}
          onChange={(value) => setHistoryRange(value as StockHistoryRange)}
          options={[
            { label: '5 лет', value: '5y' },
            { label: '3 года', value: '3y' },
            { label: '1 год', value: '1y' },
            { label: '6 мес.', value: '6m' },
            { label: '3 мес.', value: '3m' },
            { label: '1 мес.', value: '1m' },
            { label: '1 нед.', value: '1w' },
            { label: '24 ч.', value: '24h' },
            { label: 'Сегодня', value: 'today' },
          ]}
        />
      </div>
      {historyLoading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: 40 }}>
          <Spin />
        </div>
      ) : historyData.length === 0 ? (
        <Empty description="Нет данных для выбранного периода" />
      ) : (
        <div style={{ display: 'grid', gap: 12 }}>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <div
              style={{
                minWidth: 220,
                padding: '8px 12px',
                border: '1px solid #d0e8ff',
                borderRadius: 8,
                background: '#fff',
              }}
            >
              <Text type="secondary" style={{ fontSize: 12 }}>
                Изменение за период (к текущей цене)
              </Text>
              <div
                style={{
                  display: 'flex',
                  gap: 16,
                  alignItems: 'flex-end',
                  flexWrap: 'wrap',
                  marginTop: 4,
                }}
              >
                <div>
                  <div style={{ fontSize: 11, color: COLOR_SECONDARY_TEXT, marginBottom: 2 }}>
                    Тек. цена
                  </div>
                  <div style={{ color: COLOR_PRIMARY, fontSize: 16, fontWeight: 600 }}>
                    {currentPriceDisplay == null
                      ? '—'
                      : `${historyCurrencySymbol}${currentPriceDisplay.toFixed(2)}`}
                  </div>
                </div>
                <div style={{ color: performanceColor ?? 'inherit', fontWeight: 600 }}>
                  {periodChangeEur == null
                    ? '—'
                    : `${historyCurrencySymbol}${formatSigned(periodChangeEur)} (${
                        periodChangePercent == null ? '—' : formatSigned(periodChangePercent, '%')
                      })`}
                </div>
              </div>
            </div>
          </div>
          <div style={{ width: '100%', height: 240 }}>
            <ResponsiveContainer>
              <LineChart data={historyChartData}>
                <CartesianGrid strokeDasharray="3 3" />
                {historyRange === '1w' ? (
                  <XAxis
                    type="number"
                    dataKey="chartIndex"
                    scale="linear"
                    domain={['dataMin', 'dataMax']}
                    tickFormatter={(idx: number) => {
                      const ts = resolveWeeklyTs(idx);
                      return ts != null
                        ? dayjs.utc(ts).local().format(xAxisFormatByRange['1w'])
                        : '';
                    }}
                  />
                ) : (
                  <XAxis
                    type="number"
                    dataKey="timestampMs"
                    scale="time"
                    domain={['dataMin', 'dataMax']}
                    tickFormatter={(value: number) =>
                      dayjs.utc(value).local().format(xAxisFormatByRange[historyRange])
                    }
                  />
                )}
                <YAxis
                  domain={['auto', 'auto']}
                  tickFormatter={(value: number) =>
                    `${historyCurrencySymbol}${value.toFixed(2)}`
                  }
                />
                <RechartsTooltip
                  labelFormatter={(value: number) => {
                    if (historyRange === '1w') {
                      const ts = resolveWeeklyTs(value);
                      return ts != null ? dayjs.utc(ts).local().format('DD.MM.YYYY HH:mm') : '';
                    }
                    return dayjs.utc(value).local().format('DD.MM.YYYY HH:mm');
                  }}
                  formatter={(value: unknown) =>
                    value == null
                      ? ['Нет данных', 'Цена']
                      : [
                          `${historyCurrencySymbol}${Number(value).toFixed(2)}`,
                          'Цена',
                        ]
                  }
                />
                <Line
                  type="monotone"
                  dataKey="closeChart"
                  name={`Close (${historyCurrencyCode})`}
                  stroke="#1677ff"
                  dot={false}
                  strokeWidth={2}
                  connectNulls={false}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </div>
      )}
    </div>
  );
};

export default StockPriceChart;
