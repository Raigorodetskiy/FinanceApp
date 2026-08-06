import { describe, expect, it } from 'vitest';
import type { StockQuoteResponse } from '../types';
import { isQuoteDelayed, isQuoteNewerThanStored } from './quote';

const makeQuote = (overrides: Partial<StockQuoteResponse> = {}): StockQuoteResponse => ({
  symbol: 'TEST',
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
  priceTimestampUtc: '2026-08-06T13:00:00Z',
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
// isQuoteDelayed
// ──────────────────────────────────────────────────────────────────────────────

describe('isQuoteDelayed', () => {
  // Req 2: active/open market with isStale=true must be considered delayed
  it('returns true when isStale is true (open market)', () => {
    const quote = makeQuote({ isStale: true, marketState: 'REGULAR', delayWarning: null });
    expect(isQuoteDelayed(quote)).toBe(true);
  });

  // Req 2: delayWarning alone also marks the quote as delayed
  it('returns true when delayWarning is a non-empty string', () => {
    const quote = makeQuote({ isStale: false, delayWarning: 'Котировка устарела' });
    expect(isQuoteDelayed(quote)).toBe(true);
  });

  it('returns true when both isStale and delayWarning are set', () => {
    const quote = makeQuote({ isStale: true, delayWarning: 'Предупреждение' });
    expect(isQuoteDelayed(quote)).toBe(true);
  });

  // Req 2: closed market with isStale=false must NOT be marked delayed
  it('returns false for closed market with isStale=false and no delayWarning', () => {
    const quote = makeQuote({ isStale: false, marketState: 'CLOSED', delayWarning: null });
    expect(isQuoteDelayed(quote)).toBe(false);
  });

  // Req 5: backwards compatibility – new optional fields absent
  it('returns false when isStale is false and delayWarning is null', () => {
    const quote = makeQuote({ isStale: false, delayWarning: null });
    expect(isQuoteDelayed(quote)).toBe(false);
  });

  it('returns false when delayWarning is an empty string', () => {
    const quote = makeQuote({ isStale: false, delayWarning: '' });
    expect(isQuoteDelayed(quote)).toBe(false);
  });

  // Req 5: backwards-compatible when new fields are completely absent (old server)
  it('returns false when isStale and delayWarning are both absent (legacy response)', () => {
    const quote = makeQuote();
    // Simulate a legacy response where the fields do not exist at all
    const legacy = { ...quote } as Partial<StockQuoteResponse>;
    delete (legacy as Record<string, unknown>).isStale;
    delete (legacy as Record<string, unknown>).delayWarning;
    expect(isQuoteDelayed(legacy as StockQuoteResponse)).toBe(false);
  });

  it('returns false for null input', () => {
    expect(isQuoteDelayed(null)).toBe(false);
  });

  it('returns false for undefined input', () => {
    expect(isQuoteDelayed(undefined)).toBe(false);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// isQuoteNewerThanStored
// ──────────────────────────────────────────────────────────────────────────────

describe('isQuoteNewerThanStored', () => {
  // Req 3: A delayed quote with an older timestamp must not replace a newer stored value
  it('returns false when quote timestamp equals stored timestamp', () => {
    expect(
      isQuoteNewerThanStored('2026-08-06T10:00:00Z', '2026-08-06T10:00:00Z'),
    ).toBe(false);
  });

  it('returns false when quote timestamp is older than stored timestamp', () => {
    expect(
      isQuoteNewerThanStored('2026-08-06T08:00:00Z', '2026-08-06T13:00:00Z'),
    ).toBe(false);
  });

  it('returns true when quote timestamp is strictly newer than stored timestamp', () => {
    expect(
      isQuoteNewerThanStored('2026-08-06T13:31:00Z', '2026-08-06T08:02:00Z'),
    ).toBe(true);
  });

  // Req 3: null timestamps must be handled safely
  it('returns false when quote timestamp is null', () => {
    expect(isQuoteNewerThanStored(null, '2026-08-06T08:00:00Z')).toBe(false);
  });

  it('returns false when quote timestamp is undefined', () => {
    expect(isQuoteNewerThanStored(undefined, '2026-08-06T08:00:00Z')).toBe(false);
  });

  it('returns true when stored timestamp is null (no previously stored ts)', () => {
    // If we have never stored a timestamp, a quote with a timestamp is newer.
    expect(isQuoteNewerThanStored('2026-08-06T13:00:00Z', null)).toBe(true);
  });

  it('returns false when both timestamps are null', () => {
    expect(isQuoteNewerThanStored(null, null)).toBe(false);
  });

  it('returns false for invalid quote timestamp string', () => {
    expect(isQuoteNewerThanStored('not-a-date', '2026-08-06T08:00:00Z')).toBe(false);
  });
});
