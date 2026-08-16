import type { MarketIndexHistoryRange, StockHistoryRange } from '../types';

const HISTORY_RANGE_ORDER_BASE = [
  'today',
  '24h',
  '1w',
  '1m',
  '3m',
  '6m',
  '1y',
  '3y',
  '5y',
] as const;

export const HISTORY_RANGE_ORDER: StockHistoryRange[] = [...HISTORY_RANGE_ORDER_BASE];

const HISTORY_RANGE_SET = new Set<StockHistoryRange>(HISTORY_RANGE_ORDER);
export const MARKET_INDEX_HISTORY_RANGE_ORDER: MarketIndexHistoryRange[] = [...HISTORY_RANGE_ORDER_BASE];

const MARKET_INDEX_HISTORY_RANGE_SET = new Set<MarketIndexHistoryRange>(MARKET_INDEX_HISTORY_RANGE_ORDER);

export function toStockHistoryRange(
  value: string | number,
  fallback: StockHistoryRange = '1y',
): StockHistoryRange {
  const range = String(value) as StockHistoryRange;
  return HISTORY_RANGE_SET.has(range) ? range : fallback;
}

export function toMarketIndexHistoryRange(
  value: string | number,
  fallback: MarketIndexHistoryRange = '1y',
): MarketIndexHistoryRange {
  const range = String(value) as MarketIndexHistoryRange;
  return MARKET_INDEX_HISTORY_RANGE_SET.has(range) ? range : fallback;
}

const STOCK_HISTORY_LABEL_BY_RANGE: Record<StockHistoryRange, string> = {
  today: 'Сегодня',
  '24h': '24 ч.',
  '1w': '1 нед.',
  '1m': '1 мес.',
  '3m': '3 мес.',
  '6m': '6 мес.',
  '1y': '1 год',
  '3y': '3 года',
  '5y': '5 лет',
};

const MARKET_INDEX_HISTORY_LABEL_BY_RANGE: Record<MarketIndexHistoryRange, string> = {
  today: 'Сегодня',
  '24h': '24ч',
  '1w': '1н',
  '1m': '1м',
  '3m': '3м',
  '6m': '6м',
  '1y': '1г',
  '3y': '3г',
  '5y': '5г',
};

export const STOCK_HISTORY_RANGE_OPTIONS: Array<{ label: string; value: StockHistoryRange }> = HISTORY_RANGE_ORDER.map((value) => ({
  label: STOCK_HISTORY_LABEL_BY_RANGE[value],
  value,
}));

export const MARKET_INDEX_HISTORY_RANGE_OPTIONS: Array<{ label: string; value: MarketIndexHistoryRange }> = MARKET_INDEX_HISTORY_RANGE_ORDER.map((value) => ({
  label: MARKET_INDEX_HISTORY_LABEL_BY_RANGE[value],
  value,
}));
