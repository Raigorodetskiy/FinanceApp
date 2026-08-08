/**
 * Tests for day high / day low in StockQuoteResponse:
 *  – fields present on the type and nullable
 *  – formatting helpers produce the correct strings
 *  – stock / portfolio table column counts are not affected
 *  – getDayHighLowDisplay: live quote takes precedence; fallback to latest completed 1d candle;
 *    incomplete today-candle is not used; independently missing high/low; labels/date provenance
 *  – font-size constants: 14px for high/low values, 16px for current price
 */
import { describe, expect, it } from 'vitest';
import type { StockHistoryPoint, StockQuoteResponse } from '../types';
import { STOCKS_TABLE_TOTAL_COLS } from '../pages/StocksPage';
import {
  PORTFOLIO_POSITION_RIGHT_ALIGNED_MONEY_KEYS,
  PORTFOLIO_PENDING_ORDER_RIGHT_ALIGNED_MONEY_KEYS,
  PORTFOLIO_EXECUTED_ORDER_RIGHT_ALIGNED_MONEY_KEYS,
  PORTFOLIO_TRANSACTION_RIGHT_ALIGNED_MONEY_KEYS,
} from '../pages/PortfolioDetailPage';
import {
  DAY_HIGH_LOW_VALUE_FONT_SIZE,
  CURRENT_PRICE_FONT_SIZE,
  DAY_HIGH_LIVE_LABEL,
  DAY_LOW_LIVE_LABEL,
  DAY_HIGH_FALLBACK_LABEL,
  DAY_LOW_FALLBACK_LABEL,
  getDayHighLowDisplay,
  getLatestCompletedDailyCandle,
} from './dayHighLow';

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

const makePoint = (overrides: Partial<StockHistoryPoint> = {}): StockHistoryPoint => ({
  timestamp: '2025-08-01T00:00:00Z',
  interval: '1d',
  openRaw: 100,
  highRaw: 110,
  lowRaw: 95,
  closeRaw: 105,
  openNormalized: 100,
  highNormalized: 110,
  lowNormalized: 95,
  closeNormalized: 105,
  openEur: 100,
  highEur: 110,
  lowEur: 95,
  closeEur: 105,
  volume: 1000,
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
    expect(quote.rawDayHigh).not.toBeNull();
    expect(quote.rawDayLow).not.toBeNull();
    expect(quote.normalizedDayHigh).not.toBeNull();
    expect(quote.normalizedDayLow).not.toBeNull();
    expect(quote.dayHighEur).not.toBeNull();
    expect(quote.dayLowEur).not.toBeNull();
  });

  it('day high/low are independent of current price', () => {
    const quote = makeQuote({ rawDayHigh: 518.0, rawDayLow: 510.0 });
    expect(quote.rawDayHigh).toBe(518.0);
    expect(quote.rawCurrentPrice).toBe(520.5);
  });
});

// ── font-size constants ───────────────────────────────────────────────────────

describe('font-size constants', () => {
  it('DAY_HIGH_LOW_VALUE_FONT_SIZE is 14px', () => {
    expect(DAY_HIGH_LOW_VALUE_FONT_SIZE).toBe(14);
  });

  it('CURRENT_PRICE_FONT_SIZE is 16px (unchanged)', () => {
    expect(CURRENT_PRICE_FONT_SIZE).toBe(16);
  });

  it('high/low font is smaller than current-price font', () => {
    expect(DAY_HIGH_LOW_VALUE_FONT_SIZE).toBeLessThan(CURRENT_PRICE_FONT_SIZE);
  });
});

// ── getLatestCompletedDailyCandle ─────────────────────────────────────────────

describe('getLatestCompletedDailyCandle', () => {
  it('returns null when there are no points', () => {
    expect(getLatestCompletedDailyCandle([], '2026-08-09')).toBeNull();
  });

  it('returns null when all points are non-1d intervals', () => {
    const points = [makePoint({ interval: '5m', timestamp: '2026-08-08T10:00:00Z' })];
    expect(getLatestCompletedDailyCandle(points, '2026-08-09')).toBeNull();
  });

  it('returns null when the only 1d candle is today (incomplete)', () => {
    // today is 2026-08-09, candle is also 2026-08-09 → not completed
    const points = [makePoint({ interval: '1d', timestamp: '2026-08-09T00:00:00Z' })];
    expect(getLatestCompletedDailyCandle(points, '2026-08-09')).toBeNull();
  });

  it('returns the latest completed candle when today is a weekend (Saturday)', () => {
    // Saturday 2026-08-08, Friday 2026-08-07 is the last completed candle
    const friday = makePoint({ interval: '1d', timestamp: '2026-08-07T00:00:00Z', highEur: 300 });
    const thursday = makePoint({ interval: '1d', timestamp: '2026-08-06T00:00:00Z', highEur: 290 });
    const points = [thursday, friday];
    const result = getLatestCompletedDailyCandle(points, '2026-08-08');
    expect(result).toBe(friday);
    expect(result?.highEur).toBe(300);
  });

  it('skips a today-dated incomplete candle and returns the previous completed one', () => {
    const today = makePoint({ interval: '1d', timestamp: '2026-08-09T00:00:00Z', highEur: 999 });
    const yesterday = makePoint({ interval: '1d', timestamp: '2026-08-08T00:00:00Z', highEur: 310 });
    const points = [yesterday, today];
    const result = getLatestCompletedDailyCandle(points, '2026-08-09');
    expect(result).toBe(yesterday);
    expect(result?.highEur).toBe(310);
  });

  it('returns the latest among multiple completed candles', () => {
    const aug6 = makePoint({ interval: '1d', timestamp: '2026-08-06T00:00:00Z' });
    const aug7 = makePoint({ interval: '1d', timestamp: '2026-08-07T00:00:00Z' });
    const aug5 = makePoint({ interval: '1d', timestamp: '2026-08-05T00:00:00Z' });
    const result = getLatestCompletedDailyCandle([aug5, aug6, aug7], '2026-08-09');
    expect(result).toBe(aug7);
  });

  it('ignores non-1d candles even when they predate today', () => {
    const daily = makePoint({ interval: '1d', timestamp: '2026-08-07T00:00:00Z', highEur: 200 });
    const intraday = makePoint({ interval: '5m', timestamp: '2026-08-08T09:00:00Z', highEur: 999 });
    const result = getLatestCompletedDailyCandle([daily, intraday], '2026-08-09');
    expect(result).toBe(daily);
  });
});

// ── getDayHighLowDisplay – live quote takes precedence ────────────────────────

describe('getDayHighLowDisplay – live quote values take precedence', () => {
  it('uses quote dayHighEur / dayLowEur when historyHasEurConversion=true', () => {
    const quote = makeQuote({ dayHighEur: 530, dayLowEur: 500 });
    const result = getDayHighLowDisplay(quote, [], true, '2026-08-09');
    expect(result.high.value).toBe(530);
    expect(result.low.value).toBe(500);
    expect(result.high.isFromHistory).toBe(false);
    expect(result.low.isFromHistory).toBe(false);
  });

  it('uses quote normalizedDayHigh/Low when historyHasEurConversion=false', () => {
    const quote = makeQuote({ normalizedDayHigh: 528, normalizedDayLow: 502 });
    const result = getDayHighLowDisplay(quote, [], false, '2026-08-09');
    expect(result.high.value).toBe(528);
    expect(result.low.value).toBe(502);
    expect(result.high.isFromHistory).toBe(false);
    expect(result.low.isFromHistory).toBe(false);
  });

  it('falls back to rawDayHigh/Low when normalized is null but raw is present (no EUR)', () => {
    const quote = makeQuote({ rawDayHigh: 525, rawDayLow: 505, normalizedDayHigh: null, normalizedDayLow: null });
    const result = getDayHighLowDisplay(quote, [], false, '2026-08-09');
    expect(result.high.value).toBe(525);
    expect(result.low.value).toBe(505);
    expect(result.high.isFromHistory).toBe(false);
    expect(result.low.isFromHistory).toBe(false);
  });

  it('assigns live labels when quote values are present', () => {
    const quote = makeQuote({ dayHighEur: 530, dayLowEur: 500 });
    const result = getDayHighLowDisplay(quote, [], true, '2026-08-09');
    expect(result.high.label).toBe(DAY_HIGH_LIVE_LABEL);
    expect(result.low.label).toBe(DAY_LOW_LIVE_LABEL);
  });

  it('sets fallbackDate to null for live quote values', () => {
    const quote = makeQuote({ dayHighEur: 530, dayLowEur: 500 });
    const result = getDayHighLowDisplay(quote, [], true, '2026-08-09');
    expect(result.high.fallbackDate).toBeNull();
    expect(result.low.fallbackDate).toBeNull();
  });

  it('does not use history candle when both quote values are present', () => {
    const quote = makeQuote({ dayHighEur: 530, dayLowEur: 500 });
    const candle = makePoint({ interval: '1d', timestamp: '2026-08-07T00:00:00Z', highEur: 999, lowEur: 1 });
    const result = getDayHighLowDisplay(quote, [candle], true, '2026-08-09');
    expect(result.high.value).toBe(530);
    expect(result.low.value).toBe(500);
  });
});

// ── getDayHighLowDisplay – closed-market / weekend fallback ───────────────────

describe('getDayHighLowDisplay – closed-market / weekend fallback', () => {
  it('falls back to history candle EUR values when quote high/low are null and EUR conversion available', () => {
    const friday = makePoint({ interval: '1d', timestamp: '2026-08-07T00:00:00Z', highEur: 320, lowEur: 295 });
    const result = getDayHighLowDisplay(null, [friday], true, '2026-08-08'); // Saturday
    expect(result.high.value).toBe(320);
    expect(result.low.value).toBe(295);
    expect(result.high.isFromHistory).toBe(true);
    expect(result.low.isFromHistory).toBe(true);
  });

  it('falls back to history candle normalized values when no EUR conversion', () => {
    const friday = makePoint({
      interval: '1d',
      timestamp: '2026-08-07T00:00:00Z',
      highNormalized: 318,
      lowNormalized: 293,
      highEur: null,
      lowEur: null,
    });
    const result = getDayHighLowDisplay(null, [friday], false, '2026-08-08');
    expect(result.high.value).toBe(318);
    expect(result.low.value).toBe(293);
  });

  it('uses highRaw/lowRaw when normalized and EUR are both null', () => {
    const fridayNullNormalized = makePoint({
      interval: '1d',
      timestamp: '2026-08-07T00:00:00Z',
      highNormalized: null as unknown as number,
      lowNormalized: null as unknown as number,
      highEur: null,
      lowEur: null,
      highRaw: 315,
      lowRaw: 290,
    });
    const result = getDayHighLowDisplay(null, [fridayNullNormalized], false, '2026-08-08');
    expect(result.high.value).toBe(315);
    expect(result.low.value).toBe(290);
  });

  it('assigns fallback labels when using history candle', () => {
    const friday = makePoint({ interval: '1d', timestamp: '2026-08-07T00:00:00Z', highEur: 320, lowEur: 295 });
    const result = getDayHighLowDisplay(null, [friday], true, '2026-08-08');
    expect(result.high.label).toBe(DAY_HIGH_FALLBACK_LABEL);
    expect(result.low.label).toBe(DAY_LOW_FALLBACK_LABEL);
  });

  it('sets fallbackDate to the candle date (YYYY-MM-DD)', () => {
    const friday = makePoint({ interval: '1d', timestamp: '2026-08-07T00:00:00Z', highEur: 320, lowEur: 295 });
    const result = getDayHighLowDisplay(null, [friday], true, '2026-08-08');
    expect(result.high.fallbackDate).toBe('2026-08-07');
    expect(result.low.fallbackDate).toBe('2026-08-07');
  });

  it('returns null value and fallback label when no history is available either', () => {
    const result = getDayHighLowDisplay(null, [], true, '2026-08-08');
    expect(result.high.value).toBeNull();
    expect(result.low.value).toBeNull();
    expect(result.high.label).toBe(DAY_HIGH_FALLBACK_LABEL);
    expect(result.low.label).toBe(DAY_LOW_FALLBACK_LABEL);
    expect(result.high.fallbackDate).toBeNull();
    expect(result.low.fallbackDate).toBeNull();
  });
});

// ── getDayHighLowDisplay – incomplete current candle not used ─────────────────

describe('getDayHighLowDisplay – incomplete today candle is not used', () => {
  it('skips a 1d candle timestamped today and falls back to the previous completed candle', () => {
    const today = makePoint({ interval: '1d', timestamp: '2026-08-09T00:00:00Z', highEur: 999, lowEur: 1 });
    const yesterday = makePoint({ interval: '1d', timestamp: '2026-08-08T00:00:00Z', highEur: 320, lowEur: 295 });
    const result = getDayHighLowDisplay(null, [today, yesterday], true, '2026-08-09');
    expect(result.high.value).toBe(320);
    expect(result.high.fallbackDate).toBe('2026-08-08');
  });

  it('returns null value when the only 1d candle is today (market open, no quote data)', () => {
    const today = makePoint({ interval: '1d', timestamp: '2026-08-09T00:00:00Z', highEur: 999, lowEur: 1 });
    const result = getDayHighLowDisplay(null, [today], true, '2026-08-09');
    expect(result.high.value).toBeNull();
    expect(result.low.value).toBeNull();
  });
});

// ── getDayHighLowDisplay – independently missing high/low ─────────────────────

describe('getDayHighLowDisplay – independently missing high/low', () => {
  it('uses quote high when present but falls back to history for missing low', () => {
    const quote = makeQuote({ dayHighEur: 530, dayLowEur: null });
    const friday = makePoint({ interval: '1d', timestamp: '2026-08-07T00:00:00Z', highEur: 999, lowEur: 295 });
    const result = getDayHighLowDisplay(quote, [friday], true, '2026-08-08');
    // High from quote
    expect(result.high.value).toBe(530);
    expect(result.high.label).toBe(DAY_HIGH_LIVE_LABEL);
    expect(result.high.isFromHistory).toBe(false);
    // Low from history
    expect(result.low.value).toBe(295);
    expect(result.low.label).toBe(DAY_LOW_FALLBACK_LABEL);
    expect(result.low.isFromHistory).toBe(true);
    expect(result.low.fallbackDate).toBe('2026-08-07');
  });

  it('uses quote low when present but falls back to history for missing high', () => {
    const quote = makeQuote({ dayHighEur: null, dayLowEur: 500 });
    const friday = makePoint({ interval: '1d', timestamp: '2026-08-07T00:00:00Z', highEur: 320, lowEur: 1 });
    const result = getDayHighLowDisplay(quote, [friday], true, '2026-08-08');
    // High from history
    expect(result.high.value).toBe(320);
    expect(result.high.label).toBe(DAY_HIGH_FALLBACK_LABEL);
    expect(result.high.isFromHistory).toBe(true);
    // Low from quote
    expect(result.low.value).toBe(500);
    expect(result.low.label).toBe(DAY_LOW_LIVE_LABEL);
    expect(result.low.isFromHistory).toBe(false);
  });
});

// ── rawValue for unit-multiplier tooltip ──────────────────────────────────────

describe('getDayHighLowDisplay – rawValue for unit-multiplier tooltip', () => {
  it('sets rawValue from quote when using live quote high', () => {
    const quote = makeQuote({ rawDayHigh: 53000, normalizedDayHigh: 530, dayHighEur: 530 });
    const result = getDayHighLowDisplay(quote, [], true, '2026-08-09');
    expect(result.high.rawValue).toBe(53000);
  });

  it('sets rawValue to null when using history fallback', () => {
    const friday = makePoint({ interval: '1d', timestamp: '2026-08-07T00:00:00Z', highEur: 320 });
    const result = getDayHighLowDisplay(null, [friday], true, '2026-08-08');
    expect(result.high.rawValue).toBeNull();
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
