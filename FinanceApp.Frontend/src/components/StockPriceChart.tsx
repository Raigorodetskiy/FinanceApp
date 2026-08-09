import React, { useState, useEffect, useCallback, useMemo } from 'react';
import { Segmented, Spin, Typography, Empty, Alert, Button, Tooltip, message, Popconfirm } from 'antd';
import { LinkOutlined, ReloadOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import utc from 'dayjs/plugin/utc';
import {
  Bar,
  BarChart,
  LineChart,
  Line,
  CartesianGrid,
  XAxis,
  YAxis,
  Tooltip as RechartsTooltip,
  ResponsiveContainer,
} from 'recharts';
import { getStockHistory, refreshStockHistory } from '../services/api';
import {
  getStockPriceChartSummary,
} from './stockPriceChartSummary';
import { buildHistoryChartData } from './stockPriceChartData';
import type { HistoryChartPoint } from './stockPriceChartData';
import { buildFinanzenNetUrl } from '../utils/finanzenNet';
import {
  DAY_HIGH_LOW_VALUE_FONT_SIZE,
  CURRENT_PRICE_FONT_SIZE,
  getDayHighLowDisplay,
  getDayRangeLabel,
} from './dayHighLow';
import type {
  StockHistoryRange,
  StockHistoryResponse,
  StockQuoteResponse,
} from '../types';
import './StockPriceChart.css';

dayjs.extend(utc);

const { Text } = Typography;

const COLOR_POSITIVE = '#389e0d';
const COLOR_NEGATIVE = '#cf1322';
const COLOR_PRIMARY = '#1677ff';
const COLOR_SECONDARY_TEXT = '#8c8c8c';
const COLOR_VOLUME = '#91caff';
export const RANGE_BOUND_COLOR = COLOR_SECONDARY_TEXT;
export const DAY_RANGE_ARROW_TEXT = ' → ';

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

export interface StockPriceChartProps {
  panelId: string;
  stockId: number;
  ticker: string;
  name: string;
  wkn?: string | null;
  isin?: string | null;
  finanzenNetSlug?: string | null;
  liveQuote?: StockQuoteResponse | null;
  storedPriceEur?: number | null;
  storedPriceChangeEur?: number | null;
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

const formatNumber = (value: number, maximumFractionDigits = 2): string =>
  new Intl.NumberFormat('ru-RU', { maximumFractionDigits }).format(value);

const formatCompactNumber = (value: number): string =>
  new Intl.NumberFormat('ru-RU', { notation: 'compact', maximumFractionDigits: 1 }).format(value);

const StockPriceChart: React.FC<StockPriceChartProps> = ({
  panelId,
  stockId,
  ticker,
  name,
  wkn,
  isin,
  finanzenNetSlug,
  liveQuote,
  storedPriceEur,
  storedPriceChangeEur,
}) => {
  const [historyRange, setHistoryRange] = useState<StockHistoryRange>('1y');
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyRefreshing, setHistoryRefreshing] = useState(false);
  const [historyResponse, setHistoryResponse] = useState<StockHistoryResponse | null>(null);

  const finanzenNetUrl = useMemo(() => buildFinanzenNetUrl(finanzenNetSlug), [finanzenNetSlug]);

  const fetchHistory = useCallback(async () => {
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
  }, [historyRange, stockId]);

  useEffect(() => {
    fetchHistory();
  }, [fetchHistory]);

  const handleRefreshHistory = useCallback(async () => {
    if (historyRefreshing) {
      return;
    }

    setHistoryRefreshing(true);
    try {
      const refreshRes = await refreshStockHistory(stockId);
      await fetchHistory();
      const { deletedPoints, importedPoints } = refreshRes.data;
      message.success(`История перезагружена: удалено ${deletedPoints}, загружено ${importedPoints}`);
    } catch (error: unknown) {
      const errorMessage =
        error != null &&
        typeof error === 'object' &&
        'response' in error &&
        error.response != null &&
        typeof error.response === 'object' &&
        'data' in error.response &&
        typeof error.response.data === 'string'
          ? error.response.data
          : 'Не удалось перезагрузить историю';
      message.error(errorMessage);
    } finally {
      setHistoryRefreshing(false);
    }
  }, [fetchHistory, historyRefreshing, stockId]);

  const historyData = historyResponse?.points ?? [];
  const historyHasEurConversion = historyResponse?.rateToEur != null;
  const historyCurrencyCode = historyHasEurConversion
    ? 'EUR'
    : historyResponse?.normalizedQuoteCurrency ?? historyResponse?.currency ?? null;
  const volumeMetrics = historyResponse?.volumeMetrics ?? null;

  const historyChartData = useMemo(() => buildHistoryChartData(historyData, historyRange), [historyData, historyRange]);

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

  const periodSummary = useMemo(
    () => getStockPriceChartSummary({
      historyRange,
      currentPriceDisplayValue,
      firstHistoryClose,
      historyHasEurConversion,
      liveQuote,
      storedPriceEur,
      storedPriceChangeEur,
    }),
    [
      currentPriceDisplayValue,
      firstHistoryClose,
      historyHasEurConversion,
      historyRange,
      liveQuote,
      storedPriceChangeEur,
      storedPriceEur,
    ],
  );
  const periodChangeValue = periodSummary.changeValue;
  const periodChangePercent = periodSummary.changePercent;
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

  const dayHighLowDisplay = useMemo(
    () => getDayHighLowDisplay(liveQuote, historyData, historyHasEurConversion),
    [liveQuote, historyData, historyHasEurConversion],
  );
  const latestVolumePoint = useMemo(() => {
    if (!volumeMetrics?.latestMetricsTimestamp) {
      return null;
    }

    return historyData.find((point) => point.timestamp === volumeMetrics.latestMetricsTimestamp) ?? null;
  }, [historyData, volumeMetrics?.latestMetricsTimestamp]);
  const volumeMetricItems = useMemo(() => [
    {
      key: 'volume',
      label: 'Объём',
      tooltip: volumeMetrics?.usesCompletedCandle
        ? 'Объём последней завершённой свечи в выбранном диапазоне.'
        : 'Объём последней доступной свечи в выбранном диапазоне.',
      value: latestVolumePoint == null ? '—' : formatNumber(latestVolumePoint.volume, 0),
    },
    {
      key: 'averageVolume20',
      label: 'Ø20',
      tooltip: 'Средний объём за последние 20 свечей выбранного диапазона.',
      value: volumeMetrics?.averageVolume20 == null ? '—' : formatNumber(volumeMetrics.averageVolume20),
    },
    {
      key: 'averageVolume50',
      label: 'Ø50',
      tooltip: 'Средний объём за последние 50 свечей выбранного диапазона.',
      value: volumeMetrics?.averageVolume50 == null ? '—' : formatNumber(volumeMetrics.averageVolume50),
    },
    {
      key: 'relativeVolume',
      label: 'RVOL',
      tooltip: 'Относительный объём = последний объём / средний объём за 20 периодов.',
      value: volumeMetrics?.relativeVolume == null ? '—' : `${volumeMetrics.relativeVolume.toFixed(2)}x`,
    },
    {
      key: 'turnover',
      label: 'Оборот',
      tooltip: 'Цена закрытия × объём. Использует ту же валютную нормализацию, что и график цены.',
      value: volumeMetrics?.turnover == null
        ? '—'
        : formatCurrencyValue(volumeMetrics.turnover, volumeMetrics.turnoverCurrency),
    },
  ], [latestVolumePoint, volumeMetrics]);
  const renderXAxis = (hide = false) => (
    historyRange === '1w' ? (
      <XAxis
        hide={hide}
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
        hide={hide}
        type="number"
        dataKey="timestampMs"
        scale="time"
        domain={['dataMin', 'dataMax']}
        tickFormatter={(value: number) =>
          dayjs.utc(value).local().format(xAxisFormatByRange[historyRange])
        }
      />
    )
  );

  const renderDayRangeBound = (
    entry: typeof dayHighLowDisplay.minimum,
    currencyCode: string,
  ) => {
    if (entry.value == null) {
      return <span style={{ color: RANGE_BOUND_COLOR }}>—</span>;
    }

    const formatted = formatCurrencyValue(entry.value, currencyCode);

    const rawValue = entry.rawValue;
    const rawValueDiffers = rawValue != null && Math.abs(rawValue - entry.value) > 1e-9;
    const rawValueText =
      rawValueDiffers && historyResponse?.currency != null
        ? `Исходное значение: ${rawValue.toFixed(2)} ${historyResponse.currency}`
        : null;
    const timestampText =
      entry.timestampUtc != null
        ? `${entry.isFromLiveQuote ? 'Время котировки' : 'Свеча'}: ${dayjs.utc(entry.timestampUtc).local().format('DD.MM.YYYY HH:mm')}`
        : null;

    const tooltip = [timestampText, rawValueText].filter((value): value is string => value != null).join('\n');

    return tooltip !== '' ? (
      <Tooltip title={<span style={{ whiteSpace: 'pre-line' }}>{tooltip}</span>}>
        <span style={{ color: RANGE_BOUND_COLOR, cursor: 'help' }}>{formatted}</span>
      </Tooltip>
    ) : (
      <span style={{ color: RANGE_BOUND_COLOR }}>{formatted}</span>
    );
  };

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
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <Popconfirm
            title="Перезагрузить историю?"
            description="Все сохранённые исторические данные по этой акции будут удалены и загружены заново по текущему тикеру и бирже."
            okText="Перезагрузить"
            cancelText="Отмена"
            onConfirm={handleRefreshHistory}
            disabled={historyRefreshing}
          >
            <Button
              size="small"
              icon={<ReloadOutlined />}
              loading={historyRefreshing}
              disabled={historyRefreshing}
            >
              Перезагрузить историю
            </Button>
          </Popconfirm>
          <Segmented
            className="stock-price-chart-segmented"
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
              <div className="stock-price-chart-summary-header">
                <Text type="secondary" style={{ fontSize: 12 }}>
                  Изменение за период (к текущей цене)
                </Text>
                <Tooltip
                  title={
                    finanzenNetUrl
                      ? 'Открыть страницу инструмента на finanzen.net'
                      : 'Для открытия finanzen.net укажите finanzen.net Slug в настройках акции'
                  }
                >
                  <Button
                    type="link"
                    size="small"
                    icon={<LinkOutlined />}
                    disabled={!finanzenNetUrl}
                    aria-label="Открыть на finanzen.net"
                    style={{ padding: '0 4px', height: 'auto', fontSize: 12 }}
                    onClick={() => {
                      if (!finanzenNetUrl) return;
                      const popup = window.open(
                        finanzenNetUrl,
                        'finanzen_net_popup',
                        'noopener,noreferrer,width=1200,height=800,resizable=yes,scrollbars=yes',
                      );
                      if (popup) {
                        popup.opener = null;
                      }
                    }}
                  >
                    finanzen.net
                  </Button>
                </Tooltip>
              </div>
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
                  <div style={{ color: COLOR_PRIMARY, fontSize: CURRENT_PRICE_FONT_SIZE, fontWeight: 600 }}>
                    {currentPriceDisplayText}
                  </div>
                  {normalizedQuoteText && (
                    <div style={{ fontSize: 11, color: COLOR_SECONDARY_TEXT, marginTop: 2 }}>
                      Нормализовано: {normalizedQuoteText}
                    </div>
                  )}
                </div>
                <div>
                  <div style={{ fontSize: 11, color: COLOR_SECONDARY_TEXT, marginBottom: 2 }}>
                    {getDayRangeLabel(historyRange)}
                  </div>
                  <div style={{ fontSize: DAY_HIGH_LOW_VALUE_FONT_SIZE, fontWeight: 600, color: RANGE_BOUND_COLOR }}>
                    {renderDayRangeBound(dayHighLowDisplay.minimum, displayCurrencyCode)}
                    <span style={{ color: RANGE_BOUND_COLOR }}>{DAY_RANGE_ARROW_TEXT}</span>
                    {renderDayRangeBound(dayHighLowDisplay.maximum, displayCurrencyCode)}
                  </div>
                </div>
                <div>
                  <div style={{ fontSize: 11, color: COLOR_SECONDARY_TEXT, marginBottom: 2 }}>
                    {periodSummary.baselineLabel}
                  </div>
                  <div style={{ color: COLOR_SECONDARY_TEXT, fontSize: 16, fontWeight: 600 }}>
                    {periodSummary.baselineValue == null
                      ? '—'
                      : formatCurrencyValue(periodSummary.baselineValue, displayCurrencyCode)}
                  </div>
                </div>
                <div style={{ color: performanceColor ?? 'inherit', fontWeight: 600 }}>
                  {periodChangeValue == null
                    ? '—'
                    : `${formatCurrencyValue(periodChangeValue, displayCurrencyCode)} (${periodChangePercent == null ? '—' : formatSigned(periodChangePercent, '%')})`}
                </div>
              </div>
            </div>
            <div
              style={{
                minWidth: 280,
                flex: '1 1 320px',
                padding: '8px 12px',
                border: '1px solid #d0e8ff',
                borderRadius: 8,
                background: '#fff',
              }}
            >
              <div className="stock-price-chart-summary-header">
                <Text type="secondary" style={{ fontSize: 12 }}>
                  Объём и активность торгов
                </Text>
              </div>
              <div
                style={{
                  display: 'grid',
                  gridTemplateColumns: 'repeat(auto-fit, minmax(96px, 1fr))',
                  gap: 10,
                  marginTop: 8,
                }}
              >
                {volumeMetricItems.map((item) => (
                  <div key={item.key}>
                    <Tooltip title={item.tooltip}>
                      <div style={{ fontSize: 11, color: COLOR_SECONDARY_TEXT, marginBottom: 2, cursor: 'help' }}>
                        {item.label}
                      </div>
                    </Tooltip>
                    <div style={{ fontSize: 15, fontWeight: 600 }}>
                      {item.value}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
          <div style={{ width: '100%', height: 240 }}>
            <ResponsiveContainer>
              <LineChart data={historyChartData} syncId={`stock-history-${stockId}`}>
                <CartesianGrid strokeDasharray="3 3" />
                {renderXAxis()}
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
          <div style={{ width: '100%', height: 128 }}>
            <ResponsiveContainer>
              <BarChart data={historyChartData} syncId={`stock-history-${stockId}`}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} />
                {renderXAxis(true)}
                <YAxis tickFormatter={(value: number) => formatCompactNumber(value)} width={60} />
                <RechartsTooltip
                  labelFormatter={(value: number) => {
                    if (historyRange === '1w') {
                      const ts = resolveWeeklyTs(value);
                      return ts != null ? dayjs.utc(ts).local().format('DD.MM.YYYY HH:mm') : '';
                    }
                    return dayjs.utc(value).local().format('DD.MM.YYYY HH:mm');
                  }}
                  formatter={(value: unknown) => {
                    if (value == null) {
                      return ['Нет данных', 'Объём'];
                    }

                    return [formatNumber(Number(value), 0), 'Объём'];
                  }}
                />
                <Bar
                  dataKey="volumeChart"
                  name="Объём"
                  fill={COLOR_VOLUME}
                  isAnimationActive={false}
                />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
      )}
    </div>
  );
};

export default StockPriceChart;
