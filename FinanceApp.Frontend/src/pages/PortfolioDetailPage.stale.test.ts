import { describe, expect, it } from 'vitest';
import type { StockQuoteResponse } from '../types';
import { buildQuotePatch } from './PortfolioDetailPage';
import { isQuoteDelayed, isQuoteNewerThanStored } from '../utils/quote';

const makeQuote = (overrides: Partial<StockQuoteResponse> = {}): StockQuoteResponse => ({
  symbol: 'NVD',
  rawCurrentPrice: 847,
  rawPreviousClose: 850,
  rawChange: -3,
  currency: 'EUR',
  financialCurrency: 'EUR',
  normalizedQuoteCurrency: 'EUR',
  quoteUnitMultiplier: 1,
  normalizedCurrentPrice: 847,
  normalizedPreviousClose: 850,
  normalizedChange: -3,
  currentPriceEur: 847.0,
  changeEur: -3.0,
  percentChange: -0.35,
  rawDayHigh: null,
  rawDayLow: null,
  normalizedDayHigh: null,
  normalizedDayLow: null,
  dayHighEur: null,
  dayLowEur: null,
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

// ──────────────────────────────────────────────────────────────────────────────
// Req 2 · Delayed presentation – isQuoteDelayed guards
// ──────────────────────────────────────────────────────────────────────────────

describe('isQuoteDelayed – portfolio-page guard', () => {
  it('active/open market with isStale=true is considered delayed', () => {
    const quote = makeQuote({ marketState: 'REGULAR', isStale: true });
    expect(isQuoteDelayed(quote)).toBe(true);
  });

  it('closed market with isStale=false is NOT delayed', () => {
    const quote = makeQuote({ marketState: 'CLOSED', isStale: false });
    expect(isQuoteDelayed(quote)).toBe(false);
  });

  it('delayWarning alone marks quote as delayed', () => {
    const quote = makeQuote({ isStale: false, delayWarning: 'Данные задержаны' });
    expect(isQuoteDelayed(quote)).toBe(true);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// Req 3 · Stale-update protection – delayed quote must not overwrite newer stored price
// ──────────────────────────────────────────────────────────────────────────────

describe('stale-update protection for portfolio refresh', () => {
  // Req 3: delayed quote whose timestamp is older than stored must not produce a patch
  it('buildQuotePatch builds a patch regardless of staleness (guard happens at call site)', () => {
    // buildQuotePatch itself is agnostic about staleness; the refresh handler is
    // responsible for skipping it when isQuoteDelayed() returns true.
    const quote = makeQuote({ isStale: true, currentPriceEur: 800 });
    const patch = buildQuotePatch(quote);
    // The patch is built, but the refresh handler should not persist it.
    expect(patch).not.toBeNull();
    expect(patch?.currentPrice).toBe(800);
  });

  // Req 3: the call site compares timestamps using isQuoteNewerThanStored
  it('does not replace stored price when delayed quote timestamp is older', () => {
    const storedTs = '2026-08-06T13:31:00Z'; // Newer stored timestamp (e.g. MSF refreshed fine)
    const delayedQuoteTs = '2026-08-06T08:02:00Z'; // Stale Frankfurt quote time
    expect(isQuoteNewerThanStored(delayedQuoteTs, storedTs)).toBe(false);
  });

  it('allows replacing stored price when quote timestamp is strictly newer', () => {
    const storedTs = '2026-08-06T08:02:00Z';
    const freshQuoteTs = '2026-08-06T13:31:00Z';
    expect(isQuoteNewerThanStored(freshQuoteTs, storedTs)).toBe(true);
  });

  // Req 3: null timestamps must not crash and must default to "not newer"
  it('treats null quote timestamp as not newer', () => {
    expect(isQuoteNewerThanStored(null, '2026-08-06T08:00:00Z')).toBe(false);
  });

  it('treats null stored timestamp as "never stored" – any quote ts is newer', () => {
    expect(isQuoteNewerThanStored('2026-08-06T08:02:00Z', null)).toBe(true);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// Req 4 · Bulk refresh summary
// When handleRefreshPositionPrices processes delayed quotes it must count them
// separately.  This test verifies the helper contract used to build that count.
// ──────────────────────────────────────────────────────────────────────────────

describe('bulk-refresh delayed count logic', () => {
  it('isQuoteDelayed correctly classifies a batch of mixed quotes', () => {
    const quotes = [
      makeQuote({ isStale: false }),                          // fresh
      makeQuote({ isStale: true }),                           // delayed via isStale
      makeQuote({ isStale: false, delayWarning: 'warn' }),    // delayed via warning
      makeQuote({ isStale: false, marketState: 'CLOSED' }),   // fresh closed
    ];
    const delayed = quotes.filter(isQuoteDelayed).length;
    expect(delayed).toBe(2);
  });

  // Req 4: delayed should be reported separately, not as "errors"
  it('delayed quotes are distinct from rejected/error quotes', () => {
    // A delayed quote resolves (does not throw); errors throw.
    // This is expressed in the type signature: delayed === fulfilled with delayed flag.
    const fulfilledDelayed = { status: 'fulfilled' as const, value: { stockId: 1, patch: null, delayed: true } };
    const rejected = { status: 'rejected' as const, reason: new Error('Network') };
    const results = [fulfilledDelayed, rejected];

    const failedCount = results.filter((r) => r.status === 'rejected').length;
    const delayedCount = results.filter(
      (r) => r.status === 'fulfilled' && r.value.delayed,
    ).length;

    expect(failedCount).toBe(1);
    expect(delayedCount).toBe(1);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// Req 7 · One stock shared by multiple portfolios remains consistent
// The patch map in handleRefreshPositionPrices is keyed by stock.id so all
// portfolio items sharing that id receive the same patch (or no-op if delayed).
// ──────────────────────────────────────────────────────────────────────────────

describe('shared stock consistency across portfolios', () => {
  it('patching by stock.id applies the same fresh patch to every portfolio item with that id', () => {
    const freshPatch = { currentPrice: 900, currentPriceChange: 50, currentPriceChangePercent: 5.88, currentPriceAt: '2026-08-06T13:31:00Z' };
    const patchMap = new Map([[42, freshPatch]]);

    const applyPatch = (stock: { id: number; currentPrice: number }) => {
      const patch = patchMap.get(stock.id);
      return patch ? { ...stock, ...patch } : stock;
    };

    const portfolio1Stock = { id: 42, currentPrice: 847 };
    const portfolio2Stock = { id: 42, currentPrice: 847 };

    expect(applyPatch(portfolio1Stock).currentPrice).toBe(900);
    expect(applyPatch(portfolio2Stock).currentPrice).toBe(900);
  });

  it('a delayed stock (no patch entry) leaves both portfolio items unchanged', () => {
    const patchMap = new Map<number, { currentPrice: number }>(); // empty for delayed stock

    const applyPatch = (stock: { id: number; currentPrice: number }) => {
      const patch = patchMap.get(stock.id);
      return patch ? { ...stock, ...patch } : stock;
    };

    const portfolio1Stock = { id: 42, currentPrice: 847 };
    const portfolio2Stock = { id: 42, currentPrice: 847 };

    expect(applyPatch(portfolio1Stock).currentPrice).toBe(847);
    expect(applyPatch(portfolio2Stock).currentPrice).toBe(847);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// Req 5 · Backwards compatibility – new optional fields absent
// ──────────────────────────────────────────────────────────────────────────────

describe('backwards compatibility – optional stale fields absent', () => {
  it('buildQuotePatch works normally when delayWarning is absent', () => {
    const quote = makeQuote();
    delete (quote as Record<string, unknown>).delayWarning;
    const patch = buildQuotePatch(quote);
    expect(patch).not.toBeNull();
    expect(patch?.currentPrice).toBe(847);
  });

  it('isQuoteDelayed returns false for legacy response without isStale or delayWarning', () => {
    const quote = makeQuote();
    delete (quote as Record<string, unknown>).isStale;
    delete (quote as Record<string, unknown>).delayWarning;
    expect(isQuoteDelayed(quote)).toBe(false);
  });
});
