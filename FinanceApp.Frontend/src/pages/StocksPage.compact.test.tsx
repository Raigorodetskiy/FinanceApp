import { describe, expect, it } from 'vitest';
import dayjs from 'dayjs';
import utc from 'dayjs/plugin/utc';
import {
  ACTIONS_COL_WIDTH,
  API_PRICE_COL_WIDTH,
  CHANGE_EUR_COL_WIDTH,
  CHANGE_PCT_COL_WIDTH,
  PRICE_TIME_COL_WIDTH,
  PRICE_TIME_FORMAT,
  STOCKS_API_AREA_COMPACT_CLASS,
  STOCKS_CHANGE_COMPACT_CLASS,
  STOCKS_RIGHT_COMPACT_COLUMN_TITLES,
  STOCKS_TABLE_TOTAL_COLS,
  getApiPriceText,
  getApiPriceTooltip,
} from './StocksPage';
import type { StockQuoteResponse } from '../types';

dayjs.extend(utc);

const makeQuote = (overrides: Partial<StockQuoteResponse> = {}): StockQuoteResponse => ({
  symbol: 'AAPL',
  rawCurrentPrice: 123.45,
  rawPreviousClose: 120,
  rawChange: 3.45,
  currency: 'USD',
  financialCurrency: 'USD',
  normalizedQuoteCurrency: 'EUR',
  quoteUnitMultiplier: 1,
  normalizedCurrentPrice: 111.111,
  normalizedPreviousClose: 108,
  normalizedChange: 3.111,
  currentPriceEur: 111.11,
  changeEur: 3.11,
  percentChange: 2.88,
  marketState: 'REGULAR',
  priceSession: 'REGULAR',
  priceTimestampUtc: '2026-08-04T07:08:00Z',
  isStale: false,
  priceSource: 'test',
  rateToEur: 0.9,
  rateTimestampUtc: '2026-08-04T07:08:00Z',
  rateSource: 'ecb',
  conversionWarning: null,
  ...overrides,
});

describe('Stocks table – compact API area widths and invariants', () => {
  it('keeps change columns compact and unchanged', () => {
    expect(CHANGE_EUR_COL_WIDTH).toBe(85);
    expect(CHANGE_PCT_COL_WIDTH).toBe(75);
    expect(STOCKS_CHANGE_COMPACT_CLASS).toBe('stock-change-compact-col');
  });

  it('uses compact target widths for API price, time, and actions', () => {
    expect(API_PRICE_COL_WIDTH).toBe(105);
    expect(PRICE_TIME_COL_WIDTH).toBe(135);
    expect(ACTIONS_COL_WIDTH).toBe(180);
  });

  it('keeps total leaf column count at 8', () => {
    expect(STOCKS_TABLE_TOTAL_COLS).toBe(8);
  });
});

describe('Stocks table – compact API area metadata', () => {
  it('orders columns as Цена API, then Время, then Действия', () => {
    expect([...STOCKS_RIGHT_COMPACT_COLUMN_TITLES]).toEqual(['Цена API', 'Время', 'Действия']);
  });

  it('renames the timestamp heading to exactly Время', () => {
    expect(STOCKS_RIGHT_COMPACT_COLUMN_TITLES).toContain('Время');
    expect(STOCKS_RIGHT_COMPACT_COLUMN_TITLES).not.toContain('Время цены');
  });

  it('applies the scoped compact class to API price, time, and actions headers/cells', () => {
    expect(STOCKS_API_AREA_COMPACT_CLASS).toBe('stock-api-area-compact-col');
  });

  it('keeps expanded chart row spanning the full table width via the shared total-column constant', () => {
    expect(STOCKS_TABLE_TOTAL_COLS).toBe(8);
  });
});

describe('Stocks table – API price rendering helpers', () => {
  it('renders loaded API price with trailing currency', () => {
    expect(getApiPriceText({ loading: false, quote: makeQuote() })).toBe('123,45 USD');
  });

  it('falls back to normalized quote currency when provider currency is absent', () => {
    expect(getApiPriceText({ loading: false, quote: makeQuote({ currency: null, normalizedQuoteCurrency: 'EUR' }) }))
      .toBe('123,45 EUR');
  });

  it('shows loading placeholder while quote is being fetched', () => {
    expect(getApiPriceText({ loading: true, quote: null })).toBe('...');
  });

  it('shows em dash when no live quote exists in the current session', () => {
    expect(getApiPriceText(undefined)).toBe('—');
    expect(getApiPriceText({ loading: false, quote: null })).toBe('—');
  });

  it('preserves normalized tooltip when quote unit multiplier is not 1', () => {
    expect(getApiPriceTooltip(makeQuote({ quoteUnitMultiplier: 100, normalizedCurrentPrice: 1.234, normalizedQuoteCurrency: 'USD' })))
      .toBe('Нормализовано: 1.234 USD');
  });

  it('omits normalized tooltip for regular quotes', () => {
    expect(getApiPriceTooltip(makeQuote())).toBeUndefined();
  });
});

describe('Stocks table – timestamp format', () => {
  it('keeps the short local-time format unchanged', () => {
    expect(PRICE_TIME_FORMAT).toBe('DD.MM.YY HH:mm');
    const formatted = dayjs.utc('2026-08-04T07:08:00Z').local().format(PRICE_TIME_FORMAT);
    expect(formatted).toMatch(/\d{2}\.\d{2}\.\d{2} \d{2}:\d{2}/);
    expect(formatted).toContain('26');
  });

  it('falls back to em dash when timestamp is unavailable', () => {
    const ts: string | null = null;
    const result = ts ? dayjs.utc(ts).local().format(PRICE_TIME_FORMAT) : '—';
    expect(result).toBe('—');
  });
});
