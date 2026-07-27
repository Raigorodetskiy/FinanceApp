import React, { useState, useEffect, useCallback, useMemo } from 'react';
import { Segmented, Spin, Typography, Empty, Alert, message } from 'antd';
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
import { getStockHistory } from '../services/api';
import type {
  StockHistoryPoint,
  StockHistoryRange,
  StockHistoryResponse,
  StockQuoteResponse,
} from '../types';

dayjs.extend(utc);

const { Text } = Typography;

const SHORT_INTRADAY_GAP_THRESHOLD_MS = 2 * 60 * 60 * 1000;
const MIN_GAP_MARKER_OFFSET_MS = 1;
const historyGapThresholdMsByRange: Partial<Record<StockHistoryRange, number>> = {
  '24h': SHORT_INTRADAY_GAP_THRESHOLD_MS,
  today: SHORT_INTRADAY_GAP_THRESHOLD_MS,
};

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
  rawClose: number;
  chartIndex?: number;
};

export interface StockPriceChartProps {
  panelId: string;
  stockId: number;
  ticker: string;
  name: string;
  wkn?: string | null;
  isin?: string | null;
  liveQuote?: StockQuoteResponse | null;
  storedPriceEur?: number | null;
}

const formatSigned = (value: number, suffix = '') =>
  `${value >= 0 ? '+' : ''}${value.toFixed(2)}${suffix}`;

const formatCurrencyValue = (value: number, currencyCode: string | null | undefined): string => {
  if ((currencyCode ?? 'EUR') === 'EUR') {
    return `€${value.toFixed(2)}`;
  }

  return `${value.toFixed(2)} ${currencyCode ?? '—'}`;
};

const formatRawQuote = (quote: StockQuoteResponse): string =>
  `${quote.rawCurrentPrice.toFixed(2)} ${quote.currency ?? quote.normalizedQuoteCurrency ?? '—'}`;

const StockPriceChart: React.FC<StockPriceChartProps> = ({
  panelId,
  stockId,
  ticker,
  name,
  wkn,
  isin,
  liveQuote,
  storedPriceEur,
}) => {
  const [historyRange, setHistoryRange] = useState<StockHistoryRange>('1y');
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyResponse, setHistoryResponse] = useState<StockHistoryResponse | null>(null);

  useEffect(() => {
    const fetchHistory = async () => {
      setHistoryLoading(true);
      try {
        const res = await getStockHistory(stockId, historyRange);
        setHistoryResponse(res.data);
      } catch {
        setHistoryResponse(null);
        message.error('Ошибка загрузки исторических данных');
      } finally {
        setHistoryLoading(false);
      }
    };

    fetchHistory();
  }, [stockId, historyRange]);

  const historyData = historyResponse?.points ?? [];
  const historyHasEurConversion = historyResponse?.rateToEur != null;
  const historyCurrencyCode = historyHasEurConversion
    ? 'EUR'
    : historyResponse?.normalizedQuoteCurrency ?? historyResponse?.currency ?? null;

  const historyChartData = useMemo<HistoryChartPoint[]>(() => {
    const sortedPoints: HistoryChartPoint[] = historyData
      .map((point: StockHistoryPoint) => ({
        timestamp: point.timestamp,
        timestampMs: dayjs.utc(point.timestamp).valueOf(),
        closeChart: point.closeEur ?? point.closeNormalized,
        rawClose: point.closeRaw,
      }))
      .sort((left, right) => left.timestampMs - right.timestampMs);

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
        const gapTimestampMs = previousPoint.timestampMs + MIN_GAP_MARKER_OFFSET_MS;
        pointsWithGaps.push({
          timestamp: dayjs(gapTimestampMs).toISOString(),
          timestampMs: gapTimestampMs,
          closeChart: null,
          rawClose: previousPoint.rawClose,
        });
      }
      pointsWithGaps.push(currentPoint);
      previousPoint = currentPoint;
    }

    return pointsWithGaps;
  }, [historyData, historyRange]);

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

  const resolveWeeklyTs = useCallback(
    (idx: number) => weeklyIndexToTimestampMs.get(Math.round(idx)),
    [weeklyIndexToTimestampMs],
  );

  const firstHistoryClose = useMemo(() => {
    for (const point of historyChartData) {
      if (point.closeChart != null) return point.closeChart;
    }
    return null;
  }, [historyChartData]);

  const latestHistoryClose = useMemo(() => {
    for (let i = historyChartData.length - 1; i >= 0; i -= 1) {
      if (historyChartData[i].closeChart != null) return historyChartData[i].closeChart;
    }
    return null;
  }, [historyChartData]);

  const displayCurrencyCode = historyCurrencyCode ?? liveQuote?.normalizedQuoteCurrency ?? liveQuote?.currency ?? 'EUR';

  const currentPriceDisplayValue = useMemo(() => {
    if (historyHasEurConversion) {
      if (liveQuote?.currentPriceEur != null) return liveQuote.currentPriceEur;
      return storedPriceEur ?? latestHistoryClose;
    }

    if (liveQuote?.normalizedCurrentPrice != null) return liveQuote.normalizedCurrentPrice;
    return latestHistoryClose;
  }, [historyHasEurConversion, latestHistoryClose, liveQuote, storedPriceEur]);

  const currentPriceDisplayText = useMemo(() => {
    if (liveQuote?.currentPriceEur != null) {
      return formatCurrencyValue(liveQuote.currentPriceEur, 'EUR');
    }

    if (!historyHasEurConversion && liveQuote != null) {
      return formatRawQuote(liveQuote);
    }

    if (currentPriceDisplayValue == null) {
      return '—';
    }

    return formatCurrencyValue(currentPriceDisplayValue, displayCurrencyCode);
  }, [currentPriceDisplayValue, displayCurrencyCode, historyHasEurConversion, liveQuote]);

  const periodChangeValue =
    currentPriceDisplayValue != null && firstHistoryClose != null
      ? currentPriceDisplayValue - firstHistoryClose
      : null;
  const periodChangePercent =
    periodChangeValue != null && firstHistoryClose != null && firstHistoryClose !== 0
      ? (periodChangeValue / firstHistoryClose) * 100
      : null;
  const performanceColor =
    periodChangeValue == null
      ? undefined
      : periodChangeValue >= 0
        ? COLOR_POSITIVE
        : COLOR_NEGATIVE;

  const warningText = liveQuote?.conversionWarning ?? historyResponse?.conversionWarning ?? null;
  const normalizedQuoteText =
    liveQuote != null && liveQuote.quoteUnitMultiplier !== 1 && liveQuote.normalizedQuoteCurrency
      ? `${liveQuote.normalizedCurrentPrice.toFixed(3)} ${liveQuote.normalizedQuoteCurrency}`
      : null;

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
          gap: 24,
          flexWrap: 'wrap',
          marginBottom: 10,
          padding: '6px 0',
        }}
      >
        <span>
          <Text type="secondary" style={{ fontSize: 12 }}>WKN: </Text>
          <Text style={{ fontSize: 13, fontWeight: 600, fontFamily: 'monospace', color: '#1677ff' }}>
            {wkn ?? '—'}
          </Text>
        </span>
        <span>
          <Text type="secondary" style={{ fontSize: 12 }}>ISIN: </Text>
          <Text style={{ fontSize: 13, fontWeight: 600, fontFamily: 'monospace', color: '#1677ff' }}>
            {isin ?? '—'}
          </Text>
        </span>
        <span>
          <Text type="secondary" style={{ fontSize: 12 }}>Валюта котировки: </Text>
          <Text style={{ fontSize: 13, fontWeight: 600 }}>
            {liveQuote?.currency ?? historyResponse?.currency ?? '—'}
          </Text>
        </span>
        <span>
          <Text type="secondary" style={{ fontSize: 12 }}>Валюта отчётности: </Text>
          <Text style={{ fontSize: 13, fontWeight: 600 }}>
            {liveQuote?.financialCurrency ?? historyResponse?.financialCurrency ?? '—'}
          </Text>
        </span>
      </div>
      {warningText && (
        <Alert
          type="warning"
          showIcon
          message={warningText}
          style={{ marginBottom: 12 }}
        />
      )}
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
                minWidth: 240,
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
                    {currentPriceDisplayText}
                  </div>
                  {normalizedQuoteText && (
                    <div style={{ fontSize: 11, color: COLOR_SECONDARY_TEXT, marginTop: 2 }}>
                      Нормализовано: {normalizedQuoteText}
                    </div>
                  )}
                </div>
                <div style={{ color: performanceColor ?? 'inherit', fontWeight: 600 }}>
                  {periodChangeValue == null
                    ? '—'
                    : `${formatCurrencyValue(periodChangeValue, displayCurrencyCode)} (${periodChangePercent == null ? '—' : formatSigned(periodChangePercent, '%')})`}
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
                    formatCurrencyValue(value, displayCurrencyCode)
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
                  formatter={(value: unknown, _name: string, item) => {
                    const payload = item.payload as HistoryChartPoint | undefined;
                    if (value == null || payload == null) {
                      return ['Нет данных', 'Цена'];
                    }

                    const formattedChartValue = formatCurrencyValue(Number(value), displayCurrencyCode);
                    if (historyHasEurConversion) {
                      return [formattedChartValue, 'Цена'];
                    }

                    return [
                      `${formattedChartValue} (raw: ${payload.rawClose.toFixed(2)} ${historyResponse?.currency ?? displayCurrencyCode ?? '—'})`,
                      'Цена',
                    ];
                  }}
                />
                <Line
                  type="monotone"
                  dataKey="closeChart"
                  name={`Close (${displayCurrencyCode ?? '—'})`}
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
