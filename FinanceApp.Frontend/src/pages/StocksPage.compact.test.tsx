import { describe, expect, it } from 'vitest';
import dayjs from 'dayjs';
import utc from 'dayjs/plugin/utc';
import { readFileSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';
import {
  ACTIONS_COL_WIDTH,
  API_PRICE_COL_WIDTH,
  CHANGE_EUR_COL_WIDTH,
  CHANGE_PCT_COL_WIDTH,
  PRICE_TIME_COL_WIDTH,
  PRICE_TIME_FORMAT,
  STOCKS_API_AREA_COMPACT_CLASS,
  STOCKS_CHANGE_COMPACT_CLASS,
  STOCKS_RIGHT_ALIGNED_MONEY_KEYS,
  STOCKS_RIGHT_COMPACT_COLUMN_TITLES,
  STOCKS_TABLE_TOTAL_COLS,
  getApiPriceText,
  getApiPriceTooltip,
  getMarketStatus,
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
  rawDayHigh: null,
  rawDayLow: null,
  normalizedDayHigh: null,
  normalizedDayLow: null,
  dayHighEur: null,
  dayLowEur: null,
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
    expect(CHANGE_EUR_COL_WIDTH).toBe(108);
    expect(CHANGE_PCT_COL_WIDTH).toBe(75);
    expect(STOCKS_CHANGE_COMPACT_CLASS).toBe('stock-change-compact-col');
  });

  it('CHANGE_EUR_COL_WIDTH is wide enough to fit "Изменение (€)" without wrapping (≥ 105 px)', () => {
    expect(CHANGE_EUR_COL_WIDTH).toBeGreaterThanOrEqual(105);
  });

  it('compact 6 px padding class is scoped to stocks-table and applied to both th and td', () => {
    expect(STOCKS_CHANGE_COMPACT_CLASS).toBe('stock-change-compact-col');
  });

  it('uses compact target widths for API price, time, and actions', () => {
    expect(API_PRICE_COL_WIDTH).toBe(130);
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

describe('Stocks table – market status helper (getMarketStatus)', () => {
  it('returns "open" for REGULAR marketState', () => {
    expect(getMarketStatus({ loading: false, quote: makeQuote({ marketState: 'REGULAR' }) })).toBe('open');
  });

  it('returns "closed" for CLOSED marketState', () => {
    expect(getMarketStatus({ loading: false, quote: makeQuote({ marketState: 'CLOSED' }) })).toBe('closed');
  });

  it('returns "closed" for POST marketState', () => {
    expect(getMarketStatus({ loading: false, quote: makeQuote({ marketState: 'POST' }) })).toBe('closed');
  });

  it('returns "closed" for PRE marketState', () => {
    expect(getMarketStatus({ loading: false, quote: makeQuote({ marketState: 'PRE' }) })).toBe('closed');
  });

  it('returns null while loading (no marker shown)', () => {
    expect(getMarketStatus({ loading: true, quote: null })).toBeNull();
  });

  it('returns null when no live quote exists (undefined)', () => {
    expect(getMarketStatus(undefined)).toBeNull();
  });

  it('returns null when live entry has no quote and is not loading', () => {
    expect(getMarketStatus({ loading: false, quote: null })).toBeNull();
  });
});

describe('Stocks table – index.css scoped nowrap rule for change headers', () => {
  const __dirname = dirname(fileURLToPath(import.meta.url));
  const cssText = readFileSync(join(__dirname, '../index.css'), 'utf-8');

  it('has a scoped white-space: nowrap rule for stocks-table change headers', () => {
    expect(cssText).toMatch(
      /\.ant-table-wrapper\.stocks-table\s+th\.stock-change-compact-col\s*\{[^}]*white-space:\s*nowrap/,
    );
  });

  it('does not apply a global nowrap to all Ant Design table headers', () => {
    expect(cssText).not.toMatch(/\.ant-table-thead\s+th\s*\{[^}]*white-space:\s*nowrap/);
  });
});


describe('index.css – table grid (растр) vertical dividers', () => {
  const __dirname = dirname(fileURLToPath(import.meta.url));
  const cssText = readFileSync(join(__dirname, '../index.css'), 'utf-8');

  it('adds a border-right rule for thead th (excluding last child)', () => {
    expect(cssText).toMatch(
      /\.ant-table-wrapper\s+\.ant-table-thead\s*>\s*tr\s*>\s*th:not\(:last-child\)/,
    );
  });

  it('adds a border-right rule for tbody td (excluding chart-panel-row and last child)', () => {
    expect(cssText).toMatch(
      /\.ant-table-wrapper\s+\.ant-table-tbody\s*>\s*tr:not\(\.chart-panel-row\)\s*>\s*td:not\(:last-child\)/,
    );
  });

  it('uses #e8e8e8 (light gray) for the vertical divider colour', () => {
    expect(cssText).toMatch(/border-right:\s*1px\s+solid\s+#e8e8e8/);
  });
});

describe('Stocks table – right-aligned monetary columns', () => {
  it('right-aligns current, daily-change, and API price columns only', () => {
    expect([...STOCKS_RIGHT_ALIGNED_MONEY_KEYS]).toEqual(['savedPrice', 'changeEur', 'apiPrice']);
    expect(STOCKS_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('changePct');
    expect(STOCKS_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('priceTime');
    expect(STOCKS_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('ticker');
  });
});
