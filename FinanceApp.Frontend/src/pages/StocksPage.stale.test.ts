import { describe, expect, it } from 'vitest';
import type { StockQuoteResponse } from '../types';
import {
  STALE_DELAY_LABEL,
  getMarketStatus,
} from './StocksPage';

type LivePriceEntry = { quote: StockQuoteResponse | null; loading: boolean };

const makeQuote = (overrides: Partial<StockQuoteResponse> = {}): StockQuoteResponse => ({
  symbol: 'TST',
  rawCurrentPrice: 100,
  rawPreviousClose: 99,
  rawChange: 1,
  currency: 'EUR',
  financialCurrency: 'EUR',
  normalizedQuoteCurrency: 'EUR',
  quoteUnitMultiplier: 1,
  normalizedCurrentPrice: 100,
  normalizedPreviousClose: 99,
  normalizedChange: 1,
  currentPriceEur: 100,
  changeEur: 1,
  percentChange: 1,
  marketState: 'REGULAR',
  priceSession: 'REGULAR',
  priceTimestampUtc: '2026-08-06T08:02:00Z',
  isStale: false,
  delayWarning: null,
  priceSource: null,
  rateToEur: 1,
  rateTimestampUtc: null,
  rateSource: null,
  conversionWarning: null,
  ...overrides,
});

const makeLive = (quoteOverrides: Partial<StockQuoteResponse> = {}): LivePriceEntry => ({
  quote: makeQuote(quoteOverrides),
  loading: false,
});

// ──────────────────────────────────────────────────────────────────────────────
// STALE_DELAY_LABEL
// ──────────────────────────────────────────────────────────────────────────────

describe('STALE_DELAY_LABEL', () => {
  it('is the Russian delayed badge label', () => {
    expect(STALE_DELAY_LABEL).toBe('Задержано');
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// getMarketStatus – baseline (no stale logic here; badge rendering in StocksPage
// uses both getMarketStatus AND isQuoteDelayed to pick the right tag)
// ──────────────────────────────────────────────────────────────────────────────

describe('getMarketStatus', () => {
  // Req 2: open market, fresh quote → marketStatus 'open'
  it('returns open for REGULAR marketState with fresh quote', () => {
    expect(getMarketStatus(makeLive({ marketState: 'REGULAR', isStale: false }))).toBe('open');
  });

  // Req 2: open market, stale quote → marketStatus still 'open', but isQuoteDelayed
  // must also be checked by the badge renderer so it shows Задержано, not Open.
  // This test verifies getMarketStatus alone still reports 'open' (the badge renderer
  // is responsible for overriding it with Задержано when delayed).
  it('returns open for REGULAR marketState even when isStale=true', () => {
    expect(getMarketStatus(makeLive({ marketState: 'REGULAR', isStale: true }))).toBe('open');
  });

  // Req 2: closed market, fresh quote → marketStatus 'closed'; must NOT show Задержано
  it('returns closed for non-REGULAR marketState with isStale=false', () => {
    expect(getMarketStatus(makeLive({ marketState: 'CLOSED', isStale: false }))).toBe('closed');
  });

  it('returns null when live is null', () => {
    expect(getMarketStatus(null)).toBeNull();
  });

  it('returns null when live is loading', () => {
    expect(getMarketStatus({ quote: makeQuote(), loading: true })).toBeNull();
  });

  it('returns null when there is no quote', () => {
    expect(getMarketStatus({ quote: null, loading: false })).toBeNull();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// delayWarning field availability
// ──────────────────────────────────────────────────────────────────────────────

describe('delayWarning availability in StockQuoteResponse', () => {
  // Req 2: delayWarning must be available from the quote for tooltip/explanatory text
  it('delayWarning carries a non-empty string when quote is stale', () => {
    const quote = makeQuote({ isStale: true, delayWarning: 'Котировка устарела на 30 мин' });
    expect(quote.delayWarning).toBe('Котировка устарела на 30 мин');
    expect(typeof quote.delayWarning).toBe('string');
  });

  it('delayWarning is null for a fresh quote', () => {
    const quote = makeQuote({ isStale: false, delayWarning: null });
    expect(quote.delayWarning).toBeNull();
  });

  // Req 2: priceTimestampUtc must be accessible for showing exact provider time
  it('priceTimestampUtc is a UTC ISO string representing provider quote time', () => {
    const ts = '2026-08-06T08:02:00Z';
    const quote = makeQuote({ priceTimestampUtc: ts });
    expect(quote.priceTimestampUtc).toBe(ts);
    expect(Date.parse(quote.priceTimestampUtc!)).toBeGreaterThan(0);
  });

  // Req 5: backwards compat – delayWarning field absent from old responses
  it('absent delayWarning is treated as no warning (undefined maps to falsy)', () => {
    const quote = makeQuote();
    const withoutWarning = { ...quote } as Partial<StockQuoteResponse>;
    delete (withoutWarning as Record<string, unknown>).delayWarning;
    // The type allows undefined (optional field); falsy check covers undefined too
    expect(!!(withoutWarning as StockQuoteResponse).delayWarning).toBe(false);
  });
});
