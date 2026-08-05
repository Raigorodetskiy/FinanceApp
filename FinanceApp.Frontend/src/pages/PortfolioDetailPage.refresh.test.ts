import { describe, expect, it } from 'vitest';
import type { StockQuoteResponse } from '../types';
import { buildQuotePatch, REFRESH_QUOTES_LABEL } from './PortfolioDetailPage';

const makeQuote = (overrides: Partial<StockQuoteResponse> = {}): StockQuoteResponse => ({
  symbol: 'AAPL',
  rawCurrentPrice: 150.0,
  rawPreviousClose: 148.0,
  rawChange: 2.0,
  currency: 'USD',
  financialCurrency: 'USD',
  normalizedQuoteCurrency: 'EUR',
  quoteUnitMultiplier: 1,
  normalizedCurrentPrice: 135.0,
  normalizedPreviousClose: 133.2,
  normalizedChange: 1.8,
  currentPriceEur: 135.12,
  changeEur: 1.7865,
  percentChange: 1.34,
  marketState: 'REGULAR',
  priceSession: 'REGULAR',
  priceTimestampUtc: '2026-08-05T12:00:00Z',
  isStale: false,
  priceSource: null,
  rateToEur: 0.9,
  rateTimestampUtc: '2026-08-05T12:00:00Z',
  rateSource: 'ecb',
  conversionWarning: null,
  ...overrides,
});

describe('REFRESH_QUOTES_LABEL', () => {
  it('is the Russian accessible label used on the icon-only button', () => {
    expect(REFRESH_QUOTES_LABEL).toBe('Обновить цены');
  });
});

describe('buildQuotePatch – null guard', () => {
  it('returns null when currentPriceEur is null (no EUR conversion available)', () => {
    expect(buildQuotePatch(makeQuote({ currentPriceEur: null }))).toBeNull();
  });

  it('returns null when currentPriceEur is undefined cast to null', () => {
    const quote = makeQuote();
    (quote as unknown as Record<string, unknown>).currentPriceEur = undefined;
    expect(buildQuotePatch(quote)).toBeNull();
  });
});

describe('buildQuotePatch – currentPrice rounding', () => {
  it('rounds currentPriceEur to 2 decimal places', () => {
    const patch = buildQuotePatch(makeQuote({ currentPriceEur: 135.126789 }));
    expect(patch?.currentPrice).toBe(135.13);
  });

  it('uses Math.round (rounds 0.5 up)', () => {
    const patch = buildQuotePatch(makeQuote({ currentPriceEur: 135.125 }));
    expect(patch?.currentPrice).toBe(135.13);
  });

  it('preserves a whole-number price', () => {
    const patch = buildQuotePatch(makeQuote({ currentPriceEur: 200 }));
    expect(patch?.currentPrice).toBe(200);
  });
});

describe('buildQuotePatch – changeEur rounding', () => {
  it('rounds changeEur to 4 decimal places', () => {
    const patch = buildQuotePatch(makeQuote({ changeEur: 1.78654321 }));
    expect(patch?.currentPriceChange).toBe(1.7865);
  });

  it('returns null for currentPriceChange when changeEur is null', () => {
    const patch = buildQuotePatch(makeQuote({ changeEur: null }));
    expect(patch?.currentPriceChange).toBeNull();
  });
});

describe('buildQuotePatch – percentChange rounding', () => {
  it('rounds percentChange to 4 decimal places', () => {
    const patch = buildQuotePatch(makeQuote({ percentChange: 1.3456789 }));
    expect(patch?.currentPriceChangePercent).toBe(1.3457);
  });

  it('preserves zero percent change', () => {
    const patch = buildQuotePatch(makeQuote({ percentChange: 0 }));
    expect(patch?.currentPriceChangePercent).toBe(0);
  });

  it('returns null for currentPriceChangePercent when percentChange is null', () => {
    const quote = makeQuote();
    (quote as unknown as Record<string, unknown>).percentChange = null;
    const patch = buildQuotePatch(quote);
    expect(patch?.currentPriceChangePercent).toBeNull();
  });
});

describe('buildQuotePatch – currentPriceAt timestamp handling', () => {
  it('returns the ISO timestamp string when priceTimestampUtc is valid', () => {
    const patch = buildQuotePatch(makeQuote({ priceTimestampUtc: '2026-08-05T12:00:00Z' }));
    expect(patch?.currentPriceAt).toBe('2026-08-05T12:00:00Z');
  });

  it('returns null when priceTimestampUtc is null', () => {
    const patch = buildQuotePatch(makeQuote({ priceTimestampUtc: null }));
    expect(patch?.currentPriceAt).toBeNull();
  });

  it('returns null when priceTimestampUtc is an invalid date string', () => {
    const patch = buildQuotePatch(makeQuote({ priceTimestampUtc: 'not-a-date' }));
    expect(patch?.currentPriceAt).toBeNull();
  });
});

describe('buildQuotePatch – full patch shape', () => {
  it('returns a patch with all four quote-owned fields', () => {
    const patch = buildQuotePatch(makeQuote());
    expect(patch).toMatchObject({
      currentPrice: expect.any(Number),
      currentPriceChange: expect.any(Number),
      currentPriceChangePercent: expect.any(Number),
      currentPriceAt: expect.any(String),
    });
  });

  it('does not include non-quote fields like ticker or name', () => {
    const patch = buildQuotePatch(makeQuote());
    expect(patch).not.toHaveProperty('ticker');
    expect(patch).not.toHaveProperty('name');
    expect(patch).not.toHaveProperty('exchange');
  });
});

describe('buildQuotePatch – partial failure: quote without EUR conversion', () => {
  it('returns null so no PATCH request is sent for that stock', () => {
    // Simulates a stock whose currency has no conversion rate available
    expect(buildQuotePatch(makeQuote({ currentPriceEur: null }))).toBeNull();
  });
});
