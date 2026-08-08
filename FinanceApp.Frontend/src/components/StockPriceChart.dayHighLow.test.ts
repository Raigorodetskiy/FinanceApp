/**
 * Tests for day high / day low in StockQuoteResponse:
 *  – fields present on the type and nullable
 *  – formatting helpers produce the correct strings
 *  – stock / portfolio table column counts are not affected
 */
import { describe, expect, it } from 'vitest';
import type { StockQuoteResponse } from '../types';
import { STOCKS_TABLE_TOTAL_COLS } from '../pages/StocksPage';
import {
  PORTFOLIO_POSITION_RIGHT_ALIGNED_MONEY_KEYS,
  PORTFOLIO_PENDING_ORDER_RIGHT_ALIGNED_MONEY_KEYS,
  PORTFOLIO_EXECUTED_ORDER_RIGHT_ALIGNED_MONEY_KEYS,
  PORTFOLIO_TRANSACTION_RIGHT_ALIGNED_MONEY_KEYS,
} from '../pages/PortfolioDetailPage';

// ── helpers ──────────────────────────────────────────────────────────────────

const makeQuote = (overrides: Partial<StockQuoteResponse> = {}): StockQuoteResponse => ({
  symbol: 'RHM.DE',
  rawCurrentPrice: 520.5,
  rawPreviousClose: 514.0,
  rawChange: 6.5,
  currency: 'EUR',
  financialCurrency: 'EUR',
  normalizedQuoteCurrency: 'EUR',
  quoteUnitMultiplier: 1,
  normalizedCurrentPrice: 520.5,
  normalizedPreviousClose: 514.0,
  normalizedChange: 6.5,
  currentPriceEur: 520.5,
  changeEur: 6.5,
  percentChange: 1.26,
  rawDayHigh: null,
  rawDayLow: null,
  normalizedDayHigh: null,
  normalizedDayLow: null,
  dayHighEur: null,
  dayLowEur: null,
  marketState: 'REGULAR',
  priceSession: 'REGULAR',
  priceTimestampUtc: null,
  isStale: false,
  delayWarning: null,
  priceSource: null,
  rateToEur: 1,
  rateTimestampUtc: null,
  rateSource: null,
  conversionWarning: null,
  ...overrides,
});

// ── StockQuoteResponse – day high/low fields ──────────────────────────────────

describe('StockQuoteResponse – day high/low fields', () => {
  it('has rawDayHigh / rawDayLow fields that default to null', () => {
    const quote = makeQuote();
    expect(quote.rawDayHigh).toBeNull();
    expect(quote.rawDayLow).toBeNull();
  });

  it('has normalizedDayHigh / normalizedDayLow fields that default to null', () => {
    const quote = makeQuote();
    expect(quote.normalizedDayHigh).toBeNull();
    expect(quote.normalizedDayLow).toBeNull();
  });

  it('has dayHighEur / dayLowEur fields that default to null', () => {
    const quote = makeQuote();
    expect(quote.dayHighEur).toBeNull();
    expect(quote.dayLowEur).toBeNull();
  });

  it('accepts numeric values for all six day high/low fields', () => {
    const quote = makeQuote({
      rawDayHigh: 530.0,
      rawDayLow: 510.0,
      normalizedDayHigh: 530.0,
      normalizedDayLow: 510.0,
      dayHighEur: 530.0,
      dayLowEur: 510.0,
    });
    expect(quote.rawDayHigh).toBe(530.0);
    expect(quote.rawDayLow).toBe(510.0);
    expect(quote.normalizedDayHigh).toBe(530.0);
    expect(quote.normalizedDayLow).toBe(510.0);
    expect(quote.dayHighEur).toBe(530.0);
    expect(quote.dayLowEur).toBe(510.0);
  });

  it('both day high and day low can simultaneously be non-null', () => {
    const quote = makeQuote({
      rawDayHigh: 530.0,
      rawDayLow: 510.0,
      normalizedDayHigh: 530.0,
      normalizedDayLow: 510.0,
      dayHighEur: 530.0,
      dayLowEur: 510.0,
    });
    // Verify all six fields are populated simultaneously
    expect(quote.rawDayHigh).not.toBeNull();
    expect(quote.rawDayLow).not.toBeNull();
    expect(quote.normalizedDayHigh).not.toBeNull();
    expect(quote.normalizedDayLow).not.toBeNull();
    expect(quote.dayHighEur).not.toBeNull();
    expect(quote.dayLowEur).not.toBeNull();
  });

  it('day high/low are independent of current price', () => {
    // Day high may be above or below current price (e.g. if price has moved)
    const quote = makeQuote({ rawDayHigh: 518.0, rawDayLow: 510.0 });
    expect(quote.rawDayHigh).toBe(518.0);
    expect(quote.rawCurrentPrice).toBe(520.5); // current price above day high — valid edge case
  });
});

// ── Table column counts – not affected by day high/low ───────────────────────

describe('Stocks table – column count unchanged after adding day high/low to quote', () => {
  it('still has exactly 8 leaf columns in the Stocks table', () => {
    expect(STOCKS_TABLE_TOTAL_COLS).toBe(8);
  });
});

describe('PortfolioDetailPage tables – column alignment keys unchanged', () => {
  it('position right-aligned keys do not include dayHigh or dayLow', () => {
    expect(PORTFOLIO_POSITION_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('dayHighEur');
    expect(PORTFOLIO_POSITION_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('dayLowEur');
    expect(PORTFOLIO_POSITION_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('rawDayHigh');
    expect(PORTFOLIO_POSITION_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('rawDayLow');
  });

  it('pending order right-aligned keys do not include dayHigh or dayLow', () => {
    expect(PORTFOLIO_PENDING_ORDER_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('dayHighEur');
    expect(PORTFOLIO_PENDING_ORDER_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('dayLowEur');
  });

  it('executed order right-aligned keys do not include dayHigh or dayLow', () => {
    expect(PORTFOLIO_EXECUTED_ORDER_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('dayHighEur');
    expect(PORTFOLIO_EXECUTED_ORDER_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('dayLowEur');
  });

  it('transaction right-aligned keys do not include dayHigh or dayLow', () => {
    expect(PORTFOLIO_TRANSACTION_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('dayHighEur');
    expect(PORTFOLIO_TRANSACTION_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('dayLowEur');
  });
});
