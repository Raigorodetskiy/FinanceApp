import { describe, expect, it } from 'vitest';
import {
  HISTORY_RANGE_ORDER,
  MARKET_INDEX_HISTORY_RANGE_ORDER,
  MARKET_INDEX_HISTORY_RANGE_OPTIONS,
  STOCK_HISTORY_RANGE_OPTIONS,
  toMarketIndexHistoryRange,
  toStockHistoryRange,
} from './historyRangeOptions';

describe('history range options contract', () => {
  const expectedOrder = ['today', '24h', '1w', '1m', '3m', '6m', '1y', '3y', '5y'];

  it('keeps semantic range order from today to 5y', () => {
    expect(HISTORY_RANGE_ORDER).toEqual(expectedOrder);
    expect(MARKET_INDEX_HISTORY_RANGE_ORDER).toEqual(expectedOrder);
    expect(STOCK_HISTORY_RANGE_OPTIONS.map((option) => option.value)).toEqual(expectedOrder);
    expect(MARKET_INDEX_HISTORY_RANGE_OPTIONS.map((option) => option.value)).toEqual(expectedOrder);
  });

  it('keeps stock labels from «Сегодня» to «5 лет»', () => {
    expect(STOCK_HISTORY_RANGE_OPTIONS.map((option) => option.label)).toEqual([
      'Сегодня',
      '24 ч.',
      '1 нед.',
      '1 мес.',
      '3 мес.',
      '6 мес.',
      '1 год',
      '3 года',
      '5 лет',
    ]);
  });

  it('maps segmented click value to the same stock range value', () => {
    for (const option of STOCK_HISTORY_RANGE_OPTIONS) {
      expect(toStockHistoryRange(option.value, '1y')).toBe(option.value);
    }
  });

  it('maps segmented click value to the same market-index range value and uses fallback for unknown values', () => {
    for (const option of MARKET_INDEX_HISTORY_RANGE_OPTIONS) {
      expect(toMarketIndexHistoryRange(option.value, '1y')).toBe(option.value);
    }
    expect(toMarketIndexHistoryRange('unknown', '3m')).toBe('3m');
  });
});
