import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Alert,
  Button,
  Empty,
  Input,
  Space,
  Spin,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd';
import {
  SearchOutlined,
  PlusOutlined,
  ReloadOutlined,
  CaretRightFilled,
  FundOutlined,
  BarChartOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import axios from 'axios';
import dayjs from 'dayjs';
import utc from 'dayjs/plugin/utc';
import {
  getIndexConstituents,
  getIndexConstituentHistoryRefreshJob,
  getStockPrice,
  refreshIndexConstituentHistory,
  refreshIndexConstituents,
  refreshIndexConstituentsHistory,
  trackStock,
} from '../services/api';
import StockExchangeTag from './StockExchangeTag';
import StockFundamentalsDrawer from './StockFundamentalsDrawer';
import StockPriceChart from './StockPriceChart';
import { formatCurrency as fmtCur, formatPercent } from '../utils/currency';
import { isQuoteDelayed } from '../utils/quote';
import type {
  IndexConstituentDto,
  IndexConstituentHistoryRefreshBatchResponse,
  IndexConstituentHistoryRefreshJobResponse,
  IndexConstituentHistoryRefreshJobState,
  IndexConstituentsRefreshResponse,
  StockQuoteResponse,
} from '../types';

const { Text } = Typography;
dayjs.extend(utc);

export const UNSUPPORTED_REFRESH_MESSAGE_FALLBACK =
  'Автоматическая загрузка состава для этого индекса не поддерживается';

export interface IndexConstituentsPanelProps {
  indexId: number;
  isArchived: boolean;
}

interface SourceMeta {
  source: string | null;
  asOfDate: string | null;
  isCuratedSnapshot: boolean;
  isStale: boolean;
}

function isObjectRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function getNonEmptyString(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}

export function isIndexConstituentsRefreshResponse(
  value: unknown,
): value is IndexConstituentsRefreshResponse {
  if (!isObjectRecord(value)) return false;
  if (typeof value.marketIndexId !== 'number') return false;
  if (typeof value.providerStatus !== 'string') return false;
  if (typeof value.added !== 'number') return false;
  if (typeof value.updated !== 'number') return false;
  if (typeof value.unchanged !== 'number') return false;
  if (typeof value.closed !== 'number') return false;
  if ('providerMessage' in value) {
    const providerMessage = value.providerMessage;
    if (providerMessage != null && typeof providerMessage !== 'string') return false;
  }
  return true;
}

function getProviderMessageFromBody(data: unknown): string | null {
  if (!isObjectRecord(data)) return null;
  return getNonEmptyString(data.providerMessage);
}

export function getErrMsg(err: unknown, fallback: string): string {
  if (axios.isAxiosError(err)) {
    const data = err.response?.data;
    const providerMessage = getProviderMessageFromBody(data);
    if (providerMessage) return providerMessage;
    const rawMessage = getNonEmptyString(data);
    if (rawMessage) return rawMessage;
    if (isObjectRecord(data)) {
      const message = getNonEmptyString(data.message);
      if (message) return message;
    }
  }
  return fallback;
}

export type RefreshResultNotice =
  | { kind: 'warning'; message: string; shouldReload: false }
  | { kind: 'success'; message: string; shouldReload: true }
  | { kind: 'error'; message: string; shouldReload: false };

export function classifyRefreshResult(
  response: IndexConstituentsRefreshResponse,
): RefreshResultNotice {
  if (response.providerStatus === 'Unsupported') {
    return {
      kind: 'warning',
      message: getNonEmptyString(response.providerMessage) ?? UNSUPPORTED_REFRESH_MESSAGE_FALLBACK,
      shouldReload: false,
    };
  }

  if (response.providerStatus === 'Success' || response.providerStatus === 'Partial') {
    const conflicts = response.conflicts ?? 0;
    const conflictsPart = conflicts > 0 ? `, конфликтов: ${conflicts}` : '';
    return {
      kind: 'success',
      message: `Добавлено: ${response.added}, без изменений: ${response.unchanged}, закрыто: ${response.closed}${conflictsPart}`,
      shouldReload: true,
    };
  }

  return {
    kind: 'error',
    message: getNonEmptyString(response.providerMessage) ?? 'Ошибка загрузки от поставщика',
    shouldReload: false,
  };
}

export function classifyRefreshError(err: unknown, fallback: string): RefreshResultNotice {
  if (axios.isAxiosError(err) && err.response?.status === 422) {
    const responseData = err.response.data;
    if (isIndexConstituentsRefreshResponse(responseData)) {
      if (responseData.providerStatus === 'Unsupported') {
        return {
          kind: 'warning',
          message:
            getNonEmptyString(responseData.providerMessage) ?? UNSUPPORTED_REFRESH_MESSAGE_FALLBACK,
          shouldReload: false,
        };
      }
      return {
        kind: 'error',
        message: getNonEmptyString(responseData.providerMessage) ?? fallback,
        shouldReload: false,
      };
    }
    return { kind: 'error', message: fallback, shouldReload: false };
  }

  return { kind: 'error', message: getErrMsg(err, fallback), shouldReload: false };
}

export function formatBatchHistorySummary(response: IndexConstituentHistoryRefreshBatchResponse): string {
  const suffix = response.stoppedDueToRateLimit
    ? ' Обновление остановлено из-за лимита/паузы поставщика.'
    : '';
  return `История акций: успешно ${response.succeeded}, ошибок ${response.failed}, лимит ${response.rateLimited}, пропущено ${response.skippedRateLimited} из ${response.total}.${suffix}`;
}

const HISTORY_JOB_POLL_INTERVAL_MS = 2500;
const HISTORY_JOB_POLL_TIMEOUT_MS = 4 * 60 * 1000;

type HistoryJobNotice = { level: 'success' | 'warning' | 'error' | 'info'; text: string; refreshChart: boolean };

export function buildHistoryJobNotice(
  ticker: string,
  state: IndexConstituentHistoryRefreshJobState,
  payload: IndexConstituentHistoryRefreshJobResponse,
): HistoryJobNotice {
  const providerError = getNonEmptyString(payload.error);
  if (state === 'Succeeded') {
    return { level: 'success', text: `Исторические данные обновлены для ${ticker}`, refreshChart: true };
  }
  if (state === 'RateLimited') {
    return {
      level: 'warning',
      text: providerError ?? `Поставщик ограничил запросы для ${ticker}. Попробуйте позже.`,
      refreshChart: false,
    };
  }
  if (state === 'Interrupted') {
    return {
      level: 'warning',
      text: providerError ?? `Обновление истории для ${ticker} прервано. Повторите попытку.`,
      refreshChart: false,
    };
  }
  return {
    level: 'error',
    text: providerError ?? `Ошибка обновления исторических данных для ${ticker}`,
    refreshChart: false,
  };
}

type LivePriceEntry = {
  quote: StockQuoteResponse | null;
  loading: boolean;
};

type ChartRow = { _isChartRow: true; _stockId: number };
type TableRow = IndexConstituentDto | ChartRow;

const isChartRow = (record: TableRow): record is ChartRow => '_isChartRow' in record;

const preserveEntry = (current: LivePriceEntry | undefined, loading: boolean): LivePriceEntry => ({
  quote: current?.quote ?? null,
  loading,
});

const TICKER_COL_WIDTH = 220;
const NAME_COL_WIDTH = 300;
const SAVED_PRICE_COL_WIDTH = 130;
const CHANGE_EUR_COL_WIDTH = 108;
const CHANGE_PCT_COL_WIDTH = 75;
const API_PRICE_COL_WIDTH = 130;
const PRICE_TIME_COL_WIDTH = 135;
const ACTIONS_COL_WIDTH = 180;
const TICKER_META_SPACE_WIDTH = 70;
const TICKER_TEXT_MAX_WIDTH = TICKER_COL_WIDTH - TICKER_META_SPACE_WIDTH;
const TABLE_SCROLL_X =
  TICKER_COL_WIDTH
  + NAME_COL_WIDTH
  + SAVED_PRICE_COL_WIDTH
  + CHANGE_EUR_COL_WIDTH
  + CHANGE_PCT_COL_WIDTH
  + API_PRICE_COL_WIDTH
  + PRICE_TIME_COL_WIDTH
  + ACTIONS_COL_WIDTH;
const PRICE_TIME_FORMAT = 'DD.MM.YY HH:mm';
const COLOR_POSITIVE = '#389e0d';
const COLOR_NEGATIVE = '#cf1322';
const STALE_DELAY_LABEL = 'Задержано';
const TRACKED_TOOLTIP = 'Уже добавлена в список акций';
const ADD_TRACKED_ARIA_LABEL = 'Добавлена в список акций';
const ADD_CATALOG_ARIA_LABEL = 'Добавить в список акций';
const FUNDAMENTALS_ARIA_LABEL = 'Фундаментальные данные';

export const INDEX_CONSTITUENTS_TOTAL_COLS = 8;

export const getConstituentTableRowKey = (record: TableRow): string =>
  isChartRow(record) ? `chart-${record._stockId}` : String(record.stockId);

export const makeConstituentRows = (
  items: IndexConstituentDto[],
  expandedStockId: number | null,
): TableRow[] => {
  const rows: TableRow[] = [];
  for (const item of items) {
    rows.push(item);
    if (expandedStockId === item.stockId) {
      rows.push({ _isChartRow: true, _stockId: item.stockId });
    }
  }
  return rows;
};

export const getTrackButtonState = (trackingStatus: string, trackingLoading: boolean) => {
  const isTracked = trackingStatus === 'Tracked';
  return {
    isTracked,
    disabled: isTracked || trackingLoading,
    loading: trackingLoading,
    ariaLabel: isTracked ? ADD_TRACKED_ARIA_LABEL : ADD_CATALOG_ARIA_LABEL,
    tooltip: isTracked ? TRACKED_TOOLTIP : 'Добавить в список акций',
  };
};

const getApiPriceCurrency = (quote: StockQuoteResponse | null | undefined): string | null =>
  quote?.currency ?? quote?.normalizedQuoteCurrency ?? null;

const getApiPriceText = (live: LivePriceEntry | null | undefined): string => {
  if (live?.loading) return '...';
  const quote = live?.quote;
  const currency = getApiPriceCurrency(quote);
  if (!quote || !currency) return '—';
  return fmtCur(quote.rawCurrentPrice, currency);
};

const getApiPriceTooltip = (quote: StockQuoteResponse | null | undefined): string | undefined =>
  quote && quote.quoteUnitMultiplier !== 1 && quote.normalizedQuoteCurrency
    ? `Нормализовано: ${quote.normalizedCurrentPrice.toFixed(3)} ${quote.normalizedQuoteCurrency}`
    : undefined;

const getMarketStatus = (live: LivePriceEntry | null | undefined): 'open' | 'closed' | null => {
  if (!live || live.loading || !live.quote) return null;
  return live.quote.marketState === 'REGULAR' ? 'open' : 'closed';
};

const IndexConstituentsPanel: React.FC<IndexConstituentsPanelProps> = ({
  indexId,
  isArchived,
}) => {
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [constituents, setConstituents] = useState<IndexConstituentDto[]>([]);
  const [sourceMeta, setSourceMeta] = useState<SourceMeta>({
    source: null,
    asOfDate: null,
    isCuratedSnapshot: false,
    isStale: false,
  });
  const [search, setSearch] = useState('');
  const [trackingId, setTrackingId] = useState<number | null>(null);
  const [historyRefreshStates, setHistoryRefreshStates] = useState<Record<number, IndexConstituentHistoryRefreshJobState>>({});
  const [batchHistoryRefreshing, setBatchHistoryRefreshing] = useState(false);
  const [chartRefreshTokens, setChartRefreshTokens] = useState<Record<number, number>>({});
  const [livePrices, setLivePrices] = useState<Record<number, LivePriceEntry>>({});
  const [expandedStockId, setExpandedStockId] = useState<number | null>(null);
  const [batchHistorySummary, setBatchHistorySummary] = useState<string | null>(null);
  const [fundamentalsStock, setFundamentalsStock] = useState<{
    id: number;
    ticker: string;
    name: string;
  } | null>(null);
  const [messageApi, contextHolder] = message.useMessage();
  const historyJobTimersRef = useRef<Map<number, ReturnType<typeof setTimeout>>>(new Map());
  const historyJobPollingRef = useRef<Map<number, { jobId: string; startedAt: number }>>(new Map());

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await getIndexConstituents(indexId);
      setConstituents(res.data.constituents);
      setExpandedStockId((prev) =>
        prev != null && res.data.constituents.some((c) => c.stockId === prev) ? prev : null,
      );
      setSourceMeta({
        source: getNonEmptyString(res.data.source) ?? null,
        asOfDate: getNonEmptyString(res.data.asOfDate) ?? null,
        isCuratedSnapshot: res.data.isCuratedSnapshot === true,
        isStale: res.data.isStale === true,
      });
    } catch (err) {
      setError(getErrMsg(err, 'Ошибка загрузки состава индекса'));
    } finally {
      setLoading(false);
    }
  }, [indexId]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const handleRefresh = async () => {
    setRefreshing(true);
    try {
      const res = await refreshIndexConstituents(indexId);
      const result = classifyRefreshResult(res.data);
      if (result.kind === 'warning') {
        void messageApi.warning(result.message);
      } else if (result.kind === 'success') {
        void messageApi.success(result.message);
      } else {
        void messageApi.error(result.message);
      }
      if (result.shouldReload) {
        await loadData();
      }
      setSourceMeta((prev) => ({
        source: getNonEmptyString(res.data.providerName) ?? prev.source,
        asOfDate: getNonEmptyString(res.data.asOfDate) ?? prev.asOfDate,
        isCuratedSnapshot: res.data.isCuratedSnapshot === true || prev.isCuratedSnapshot,
        isStale: res.data.isStale === true,
      }));
    } catch (err) {
      const result = classifyRefreshError(err, 'Ошибка обновления состава');
      if (result.kind === 'warning') {
        void messageApi.warning(result.message);
      } else {
        void messageApi.error(result.message);
      }
    } finally {
      setRefreshing(false);
    }
  };

  const handleTrack = async (constituent: IndexConstituentDto) => {
    setTrackingId(constituent.stockId);
    try {
      await trackStock(constituent.stockId);
      void messageApi.success(`«${constituent.name}» добавлена в отслеживаемые акции`);
      setConstituents((prev) =>
        prev.map((c) =>
          c.stockId === constituent.stockId ? { ...c, trackingStatus: 'Tracked' } : c,
        ),
      );
    } catch (err) {
      void messageApi.error(getErrMsg(err, 'Ошибка добавления в отслеживаемые'));
    } finally {
      setTrackingId(null);
    }
  };

  const handleFetchLivePrice = async (constituent: IndexConstituentDto) => {
    if (!constituent.ticker?.trim()) return;
    setLivePrices((prev) => ({
      ...prev,
      [constituent.stockId]: preserveEntry(prev[constituent.stockId], true),
    }));
    try {
      const priceRes = await getStockPrice(
        constituent.ticker,
        constituent.exchange,
        constituent.finanzenNetSlug,
      );
      setLivePrices((prev) => ({
        ...prev,
        [constituent.stockId]: { quote: priceRes.data, loading: false },
      }));
    } catch {
      setLivePrices((prev) => ({
        ...prev,
        [constituent.stockId]: preserveEntry(prev[constituent.stockId], false),
      }));
      void messageApi.error(`Ошибка получения цены для ${constituent.ticker}`);
    }
  };

  const clearHistoryPolling = useCallback((stockId: number) => {
    const timer = historyJobTimersRef.current.get(stockId);
    if (timer != null) {
      clearTimeout(timer);
      historyJobTimersRef.current.delete(stockId);
    }
    historyJobPollingRef.current.delete(stockId);
    setHistoryRefreshStates((prev) => {
      if (!(stockId in prev)) return prev;
      const next = { ...prev };
      delete next[stockId];
      return next;
    });
  }, []);

  const showHistoryNotice = useCallback((notice: HistoryJobNotice) => {
    if (notice.level === 'success') {
      void messageApi.success(notice.text);
      return;
    }
    if (notice.level === 'warning') {
      void messageApi.warning(notice.text);
      return;
    }
    if (notice.level === 'info') {
      void messageApi.info(notice.text);
      return;
    }
    void messageApi.error(notice.text);
  }, [messageApi]);

  const scheduleHistoryStatusPoll = useCallback((
    stockId: number,
    ticker: string,
    job: IndexConstituentHistoryRefreshJobResponse,
  ) => {
    clearHistoryPolling(stockId);
    historyJobPollingRef.current.set(stockId, { jobId: job.jobId, startedAt: Date.now() });
    setHistoryRefreshStates((prev) => ({ ...prev, [stockId]: job.state }));

    const poll = async () => {
      const current = historyJobPollingRef.current.get(stockId);
      if (!current || current.jobId !== job.jobId) {
        return;
      }

      if (Date.now() - current.startedAt > HISTORY_JOB_POLL_TIMEOUT_MS) {
        showHistoryNotice({
          level: 'error',
          text: `Время ожидания обновления истории для ${ticker} истекло. Проверьте позже.`,
          refreshChart: false,
        });
        clearHistoryPolling(stockId);
        return;
      }

      try {
        const status = await getIndexConstituentHistoryRefreshJob(indexId, stockId, job.jobId);
        const payload = status.data;
        setHistoryRefreshStates((prev) => ({ ...prev, [stockId]: payload.state }));
        if (payload.state === 'Queued' || payload.state === 'Running') {
          historyJobTimersRef.current.set(stockId, setTimeout(() => void poll(), HISTORY_JOB_POLL_INTERVAL_MS));
          return;
        }

        const notice = buildHistoryJobNotice(ticker, payload.state, payload);
        showHistoryNotice(notice);
        if (notice.refreshChart) {
          setChartRefreshTokens((prev) => ({
            ...prev,
            [stockId]: (prev[stockId] ?? 0) + 1,
          }));
        }
        clearHistoryPolling(stockId);
      } catch (err) {
        if (axios.isAxiosError(err) && err.response?.status === 404) {
          showHistoryNotice({
            level: 'warning',
            text: `Задача обновления истории для ${ticker} больше недоступна (истекла/перезапуск). Запустите снова.`,
            refreshChart: false,
          });
          clearHistoryPolling(stockId);
          return;
        }

        showHistoryNotice({
          level: 'error',
          text: getErrMsg(err, `Ошибка проверки статуса обновления истории для ${ticker}`),
          refreshChart: false,
        });
        clearHistoryPolling(stockId);
      }
    };

    historyJobTimersRef.current.set(stockId, setTimeout(() => void poll(), HISTORY_JOB_POLL_INTERVAL_MS));
  }, [clearHistoryPolling, indexId, showHistoryNotice]);

  useEffect(() => () => {
    for (const timer of historyJobTimersRef.current.values()) {
      clearTimeout(timer);
    }
    historyJobTimersRef.current.clear();
    historyJobPollingRef.current.clear();
  }, []);

  useEffect(() => {
    for (const timer of historyJobTimersRef.current.values()) {
      clearTimeout(timer);
    }
    historyJobTimersRef.current.clear();
    historyJobPollingRef.current.clear();
    setHistoryRefreshStates({});
  }, [indexId]);

  const handleRefreshConstituentHistory = async (constituent: IndexConstituentDto) => {
    if (historyJobPollingRef.current.has(constituent.stockId) || batchHistoryRefreshing) {
      return;
    }

    historyJobPollingRef.current.set(constituent.stockId, {
      jobId: 'starting',
      startedAt: Date.now(),
    });
    setHistoryRefreshStates((prev) => ({ ...prev, [constituent.stockId]: 'Queued' }));
    try {
      const response = await refreshIndexConstituentHistory(indexId, constituent.stockId);
      if (response.data.reusedActiveJob) {
        void messageApi.info(`Обновление истории для ${constituent.ticker} уже выполняется. Подключаемся к текущей задаче.`);
      }
      scheduleHistoryStatusPoll(constituent.stockId, constituent.ticker, response.data);
    } catch (err) {
      clearHistoryPolling(constituent.stockId);
      void messageApi.error(getErrMsg(err, `Ошибка запуска обновления исторических данных для ${constituent.ticker}`));
    }
  };

  const handleBatchRefreshHistory = async () => {
    if (batchHistoryRefreshing || constituents.length === 0) {
      return;
    }

    setBatchHistoryRefreshing(true);
    setBatchHistorySummary(null);
    try {
      const response = await refreshIndexConstituentsHistory(indexId);
      const summary = formatBatchHistorySummary(response.data);
      setBatchHistorySummary(summary);

      if (response.data.failed > 0 || response.data.rateLimited > 0 || response.data.skippedRateLimited > 0) {
        void messageApi.warning(summary);
      } else {
        void messageApi.success(summary);
      }

      if (expandedStockId != null) {
        setChartRefreshTokens((prev) => ({
          ...prev,
          [expandedStockId]: (prev[expandedStockId] ?? 0) + 1,
        }));
      }
    } catch (err) {
      const text = getErrMsg(err, 'Ошибка пакетного обновления исторических данных');
      setBatchHistorySummary(text);
      void messageApi.error(text);
    } finally {
      setBatchHistoryRefreshing(false);
    }
  };

  const filteredConstituents = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) return constituents;
    return constituents.filter((c) =>
      c.ticker.toLowerCase().includes(query)
      || c.name.toLowerCase().includes(query)
      || (c.commonName?.toLowerCase().includes(query) ?? false)
      || (c.wkn?.toLowerCase().includes(query) ?? false)
      || (c.isin?.toLowerCase().includes(query) ?? false)
      || (c.providerSymbol?.toLowerCase().includes(query) ?? false));
  }, [constituents, search]);

  const rows = useMemo(
    () => makeConstituentRows(filteredConstituents, expandedStockId),
    [expandedStockId, filteredConstituents],
  );

  const columns: ColumnsType<TableRow> = [
    {
      title: 'Тикер',
      key: 'ticker',
      width: TICKER_COL_WIDTH,
      render: (_, record) => {
        if (isChartRow(record)) {
          const stock = constituents.find((item) => item.stockId === record._stockId);
          const live = livePrices[record._stockId];
          return {
            children: (
              <StockPriceChart
                panelId={`chart-panel-${record._stockId}`}
                stockId={record._stockId}
                ticker={stock?.ticker ?? ''}
                name={stock?.name ?? ''}
                wkn={stock?.wkn ?? null}
                isin={stock?.isin ?? null}
                finanzenNetSlug={stock?.finanzenNetSlug ?? null}
                liveQuote={live?.quote ?? null}
                storedPriceEur={stock?.currentPrice ?? null}
                storedPriceChangeEur={stock?.currentPriceChange ?? null}
                refreshToken={chartRefreshTokens[record._stockId] ?? 0}
              />
            ),
            props: { colSpan: INDEX_CONSTITUENTS_TOTAL_COLS },
          };
        }

        const isExpanded = expandedStockId === record.stockId;
        return (
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0, whiteSpace: 'nowrap' }}>
            <Tooltip title={record.ticker}>
              <button
                type="button"
                onClick={() =>
                  setExpandedStockId((prev) => (prev === record.stockId ? null : record.stockId))}
                aria-expanded={isExpanded}
                aria-controls={`chart-panel-${record.stockId}`}
                aria-label={
                  isExpanded
                    ? `Закрыть график цены: ${record.ticker}`
                    : `Открыть график цены: ${record.ticker}`
                }
                style={{
                  padding: 0,
                  background: 'none',
                  border: 'none',
                  cursor: 'pointer',
                  fontWeight: 600,
                  color: isExpanded ? '#1677ff' : 'inherit',
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 4,
                  minWidth: 0,
                  maxWidth: TICKER_TEXT_MAX_WIDTH,
                }}
              >
                <CaretRightFilled
                  style={{
                    fontSize: 10,
                    transition: 'transform 0.2s',
                    transform: isExpanded ? 'rotate(90deg)' : 'rotate(0deg)',
                    color: '#1677ff',
                    flex: '0 0 auto',
                  }}
                />
                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {record.ticker}
                </span>
              </button>
            </Tooltip>
            <StockExchangeTag exchange={record.exchange} />
          </div>
        );
      },
    },
    {
      title: 'Название',
      dataIndex: 'name',
      key: 'name',
      width: NAME_COL_WIDTH,
      render: (name: string, record) => {
        if (isChartRow(record)) return { children: null, props: { colSpan: 0 } };
        return (
          <Space direction="vertical" size={0}>
            <Text style={{ fontSize: 13 }}>{name}</Text>
            {record.commonName && record.commonName !== name && (
              <Text type="secondary" style={{ fontSize: 11 }}>{record.commonName}</Text>
            )}
            {record.isin && (
              <Text type="secondary" style={{ fontSize: 11 }}>{record.isin}</Text>
            )}
          </Space>
        );
      },
    },
    {
      title: 'Текущая цена',
      key: 'savedPrice',
      align: 'right',
      width: SAVED_PRICE_COL_WIDTH,
      render: (_: unknown, record) => {
        if (isChartRow(record)) return { children: null, props: { colSpan: 0 } };
        return <span style={{ whiteSpace: 'nowrap', fontWeight: 500 }}>{fmtCur(record.currentPrice, '€')}</span>;
      },
    },
    {
      title: 'Изменение (€)',
      key: 'changeEur',
      width: CHANGE_EUR_COL_WIDTH,
      className: 'stock-change-compact-col',
      align: 'right',
      render: (_: unknown, record) => {
        if (isChartRow(record)) return { children: null, props: { colSpan: 0 } };
        const change = record.currentPriceChange ?? null;
        const color = change == null
          ? '#8c8c8c'
          : change > 0
            ? COLOR_POSITIVE
            : change < 0
              ? COLOR_NEGATIVE
              : '#8c8c8c';
        return (
          <span style={{ color, whiteSpace: 'nowrap' }}>
            {fmtCur(change, '€', { signed: true })}
          </span>
        );
      },
    },
    {
      title: '(%)',
      key: 'changePct',
      width: CHANGE_PCT_COL_WIDTH,
      className: 'stock-change-compact-col',
      render: (_: unknown, record) => {
        if (isChartRow(record)) return { children: null, props: { colSpan: 0 } };
        const pct = record.currentPriceChangePercent ?? null;
        const color = pct == null
          ? '#8c8c8c'
          : pct > 0
            ? COLOR_POSITIVE
            : pct < 0
              ? COLOR_NEGATIVE
              : '#8c8c8c';
        return <span style={{ color, whiteSpace: 'nowrap' }}>{formatPercent(pct)}</span>;
      },
    },
    {
      title: 'Цена API',
      key: 'apiPrice',
      align: 'right',
      width: API_PRICE_COL_WIDTH,
      className: 'stock-api-area-compact-col',
      render: (_: unknown, record) => {
        if (isChartRow(record)) return { children: null, props: { colSpan: 0 } };
        const live = livePrices[record.stockId];
        const quote = live?.quote ?? null;
        const status = getMarketStatus(live);
        const delayed = isQuoteDelayed(quote);
        const delayTooltip = (delayed && quote?.delayWarning) ? quote.delayWarning : undefined;
        return (
          <span title={getApiPriceTooltip(quote)} style={{ fontSize: 12, color: '#595959', whiteSpace: 'nowrap', display: 'inline-flex', alignItems: 'center', gap: 4 }}>
            {getApiPriceText(live)}
            {delayed ? (
              <Tooltip title={delayTooltip}>
                <Tag
                  color="orange"
                  style={{ fontSize: 10, lineHeight: '14px', padding: '0 3px', marginInlineEnd: 0 }}
                  aria-label={delayTooltip ?? STALE_DELAY_LABEL}
                >
                  {STALE_DELAY_LABEL}
                </Tag>
              </Tooltip>
            ) : (
              <>
                {status === 'open' && (
                  <Tag color="green" style={{ fontSize: 10, lineHeight: '14px', padding: '0 3px', marginInlineEnd: 0 }}>Open</Tag>
                )}
                {status === 'closed' && (
                  <Tag style={{ fontSize: 10, lineHeight: '14px', padding: '0 3px', marginInlineEnd: 0 }}>Closed</Tag>
                )}
              </>
            )}
          </span>
        );
      },
    },
    {
      title: 'Время',
      key: 'priceTime',
      width: PRICE_TIME_COL_WIDTH,
      className: 'stock-api-area-compact-col',
      render: (_: unknown, record) => {
        if (isChartRow(record)) return { children: null, props: { colSpan: 0 } };
        const ts = livePrices[record.stockId]?.quote?.priceTimestampUtc ?? record.currentPriceAt ?? null;
        if (!ts) return <span style={{ whiteSpace: 'nowrap' }}>—</span>;
        return <span style={{ whiteSpace: 'nowrap' }}>{dayjs.utc(ts).local().format(PRICE_TIME_FORMAT)}</span>;
      },
    },
    {
      title: 'Действия',
      key: 'actions',
      width: ACTIONS_COL_WIDTH,
      className: 'stock-api-area-compact-col',
      render: (_, record) => {
        if (isChartRow(record)) return { children: null, props: { colSpan: 0 } };
        const trackingLoading = trackingId === record.stockId;
        const trackButtonState = getTrackButtonState(record.trackingStatus, trackingLoading);
        const historyState = historyRefreshStates[record.stockId];
        const historyLoading = historyState === 'Queued' || historyState === 'Running';
        const historyDisabled = batchHistoryRefreshing || historyLoading || !record.ticker?.trim();

        const addButton = (
          <Button
            size="small"
            icon={<PlusOutlined />}
            aria-label={trackButtonState.ariaLabel}
            loading={trackButtonState.loading}
            disabled={trackButtonState.disabled}
            onClick={() => void handleTrack(record)}
          />
        );

        return (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
            <Button
              icon={<ReloadOutlined />}
              size="small"
              loading={livePrices[record.stockId]?.loading}
              disabled={!record.ticker?.trim() || batchHistoryRefreshing}
              onClick={() => void handleFetchLivePrice(record)}
              aria-label={`Обновить цену ${record.ticker}`}
            />
            <Tooltip title={`Обновить исторические данные ${record.ticker}`}>
              <Button
                icon={<BarChartOutlined />}
                size="small"
                loading={historyLoading}
                disabled={historyDisabled}
                onClick={() => void handleRefreshConstituentHistory(record)}
                aria-label={`Обновить исторические данные ${record.ticker}`}
              />
            </Tooltip>
            <Tooltip title={FUNDAMENTALS_ARIA_LABEL}>
              <Button
                icon={<FundOutlined />}
                size="small"
                aria-label={FUNDAMENTALS_ARIA_LABEL}
                onClick={() => setFundamentalsStock({ id: record.stockId, ticker: record.ticker, name: record.name })}
              />
            </Tooltip>
            {trackButtonState.isTracked ? (
              <Tooltip title={trackButtonState.tooltip}>
                <span>{addButton}</span>
              </Tooltip>
            ) : (
              <Tooltip title={trackButtonState.tooltip}>
                {addButton}
              </Tooltip>
            )}
          </div>
        );
      },
    },
  ];

  return (
    <div style={{ padding: '8px 0' }}>
      {contextHolder}

      {batchHistorySummary && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          message={batchHistorySummary}
        />
      )}

      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 12, flexWrap: 'wrap' }}>
        <Input
          placeholder="Поиск по тикеру, названию, WKN, ISIN…"
          prefix={<SearchOutlined />}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          allowClear
          style={{ maxWidth: 320 }}
          size="small"
        />
        {!isArchived && (
          <Button
            size="small"
            icon={<ReloadOutlined />}
            loading={refreshing}
            disabled={batchHistoryRefreshing}
            onClick={() => void handleRefresh()}
          >
            Обновить состав
          </Button>
        )}
        <Button
          size="small"
          icon={<BarChartOutlined />}
          loading={batchHistoryRefreshing}
          disabled={
            isArchived
            || constituents.length === 0
            || refreshing
            || batchHistoryRefreshing
            || Object.values(historyRefreshStates).some((state) => state === 'Queued' || state === 'Running')
          }
          onClick={() => {
            if (!window.confirm('Обновить историю всех акций текущего состава? Запросы выполняются последовательно и могут занять время.')) {
              return;
            }
            void handleBatchRefreshHistory();
          }}
        >
          Обновить историю акций
        </Button>
      </div>

      {(sourceMeta.source || sourceMeta.asOfDate) && (
        <Alert
          type={sourceMeta.isStale ? 'warning' : 'info'}
          showIcon
          style={{ marginBottom: 12 }}
          message={(
            <Space wrap size={8}>
              <Text style={{ fontSize: 12 }}>
                Источник: {sourceMeta.source ?? '—'}
                {sourceMeta.isCuratedSnapshot ? ' (Проверенный снимок)' : ''}
              </Text>
              {sourceMeta.asOfDate && (
                <Text style={{ fontSize: 12 }}>
                  As of: {new Date(sourceMeta.asOfDate).toLocaleDateString('ru-RU')}
                </Text>
              )}
            </Space>
          )}
        />
      )}

      {loading ? (
        <div style={{ padding: '24px 0', textAlign: 'center' }}>
          <Spin />
        </div>
      ) : error ? (
        <Alert type="error" message={error} showIcon />
      ) : constituents.length === 0 ? (
        <Empty
          description="Состав этого индекса не загружен. Нажмите «Обновить состав» для импорта."
          image={Empty.PRESENTED_IMAGE_SIMPLE}
        />
      ) : (
        <Table<TableRow>
          className="stocks-table"
          rowKey={getConstituentTableRowKey}
          columns={columns}
          dataSource={rows}
          tableLayout="fixed"
          scroll={{ x: TABLE_SCROLL_X }}
          rowClassName={(record) => (isChartRow(record) ? 'chart-panel-row' : '')}
          size="small"
          pagination={{ pageSize: 20, showSizeChanger: false, hideOnSinglePage: true }}
          locale={{ emptyText: 'Нет компонентов, соответствующих поиску' }}
        />
      )}

      <StockFundamentalsDrawer
        stock={fundamentalsStock}
        open={fundamentalsStock != null}
        onClose={() => setFundamentalsStock(null)}
      />
    </div>
  );
};

export default IndexConstituentsPanel;
