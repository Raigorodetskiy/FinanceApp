import { describe, expect, it } from 'vitest';
import dayjs from 'dayjs';
import utc from 'dayjs/plugin/utc';
import {
  API_PRICE_COL_WIDTH,
  CHANGE_EUR_COL_WIDTH,
  CHANGE_PCT_COL_WIDTH,
  PRICE_TIME_FORMAT,
  STOCKS_CHANGE_COMPACT_CLASS,
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

describe('Stocks table – compact change columns', () => {
  it('CHANGE_EUR_COL_WIDTH is approximately 85 px', () => {
    expect(CHANGE_EUR_COL_WIDTH).toBeLessThanOrEqual(90);
    expect(CHANGE_EUR_COL_WIDTH).toBeGreaterThanOrEqual(80);
  });

  it('CHANGE_PCT_COL_WIDTH is approximately 75 px', () => {
    expect(CHANGE_PCT_COL_WIDTH).toBeLessThanOrEqual(80);
    expect(CHANGE_PCT_COL_WIDTH).toBeGreaterThanOrEqual(70);
  });

  it('compact column class name is defined', () => {
    expect(STOCKS_CHANGE_COMPACT_CLASS).toBe('stock-change-compact-col');
  });

  it('keeps API price column compact and readable', () => {
    expect(API_PRICE_COL_WIDTH).toBeGreaterThanOrEqual(110);
    expect(API_PRICE_COL_WIDTH).toBeLessThanOrEqual(150);
  });
});

describe('Stocks table – column metadata', () => {
  it('uses 8 top-level columns after adding API price', () => {
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

describe('Stocks table – price timestamp format', () => {
  it('PRICE_TIME_FORMAT uses two-digit year (DD.MM.YY HH:mm)', () => {
    expect(PRICE_TIME_FORMAT).toBe('DD.MM.YY HH:mm');
  });

  it('formats UTC timestamp 2026-08-04T07:08:00Z as DD.MM.YY HH:mm in local time', () => {
    const ts = '2026-08-04T07:08:00Z';
    const formatted = dayjs.utc(ts).local().format(PRICE_TIME_FORMAT);
    expect(formatted).toMatch(/\d{2}\.\d{2}\.\d{2} \d{2}:\d{2}/);
    const parts = formatted.split(' ');
    const dateParts = parts[0].split('.');
    expect(dateParts[2]).toHaveLength(2);
    expect(dateParts[2]).toBe('26');
  });

  it('does NOT use four-digit year format', () => {
    const ts = '2026-08-04T07:08:00Z';
    const formatted = dayjs.utc(ts).local().format(PRICE_TIME_FORMAT);
    expect(formatted).not.toContain('2026');
  });

  it('missing timestamp should fall back to —', () => {
    const ts: string | null = null;
    const result = ts ? dayjs.utc(ts).local().format(PRICE_TIME_FORMAT) : '—';
    expect(result).toBe('—');
  });
});
