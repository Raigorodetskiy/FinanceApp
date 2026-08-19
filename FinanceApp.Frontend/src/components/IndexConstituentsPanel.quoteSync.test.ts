import { describe, expect, it, vi } from 'vitest';
import type {
  IndexConstituentDto,
  StockQuoteResponse,
  UpdateStockQuoteRequest,
  UpdateStockQuoteResponse,
} from '../types';
import {
  QUOTE_NO_EUR_MESSAGE,
  beginConstituentQuoteRefresh,
  finishConstituentQuoteRefresh,
  getNoEurQuoteMessage,
  persistFreshConstituentQuote,
} from './IndexConstituentsPanel';
import { applyPersistedQuoteSnapshot } from '../utils/quotePersistence';

const makeConstituent = (overrides: Partial<IndexConstituentDto> = {}): IndexConstituentDto => ({
  stockId: 7,
  ticker: 'AAPL',
  name: 'Apple Inc.',
  commonName: 'Apple',
  exchange: 'NASDAQ',
  currentPrice: 99.99,
  currentPriceChange: 0.5,
  currentPriceChangePercent: 0.5,
  currentPriceAt: '2026-08-17T09:00:00Z',
  currentPriceIsDelayed: false,
  currentPriceDelayWarning: null,
  importedAt: '2026-08-17T00:00:00Z',
  trackingStatus: 'CatalogOnly',
  ...overrides,
});

const makeQuote = (overrides: Partial<StockQuoteResponse> = {}): StockQuoteResponse => ({
  symbol: 'AAPL',
  rawCurrentPrice: 123.456,
  rawPreviousClose: 120,
  rawChange: 3.456,
  currency: 'USD',
  financialCurrency: 'USD',
  normalizedQuoteCurrency: 'USD',
  quoteUnitMultiplier: 1,
  normalizedCurrentPrice: 123.456,
  normalizedPreviousClose: 120,
  normalizedChange: 3.456,
  currentPriceEur: 111.234,
  changeEur: 1.23456,
  percentChange: 1.98765,
  rawDayHigh: null,
  rawDayLow: null,
  normalizedDayHigh: null,
  normalizedDayLow: null,
  dayHighEur: null,
  dayLowEur: null,
  marketState: 'REGULAR',
  priceSession: 'REGULAR',
  priceTimestampUtc: '2026-08-17T10:11:12Z',
  isStale: false,
  delayWarning: null,
  priceSource: null,
  rateToEur: 0.9,
  rateTimestampUtc: '2026-08-17T10:11:12Z',
  rateSource: 'ecb',
  conversionWarning: null,
  ...overrides,
});

const makePersisted = (
  overrides: Partial<UpdateStockQuoteResponse> = {},
): UpdateStockQuoteResponse => ({
  stockId: 7,
  currentPrice: 111.23,
  currentPriceChange: 1.2346,
  currentPriceChangePercent: 1.9877,
  currentPriceAt: '2026-08-17T10:11:12Z',
  currentPriceIsDelayed: false,
  currentPriceDelayWarning: null,
  applied: true,
  ...overrides,
});

describe('IndexConstituentsPanel quote persistence helpers', () => {
  it('persists fresh EUR quote with StocksPage-compatible rounding and provider timestamp', async () => {
    const constituent = makeConstituent();
    const persisted = makePersisted();
    const persistQuote = vi.fn(
      async (stockId: number, patch: UpdateStockQuoteRequest) => {
        expect(stockId).toBe(constituent.stockId);
        expect(patch).toEqual({
          currentPrice: 111.23,
          currentPriceChange: 1.2346,
          currentPriceChangePercent: 1.9877,
          currentPriceAt: '2026-08-17T10:11:12Z',
          currentPriceIsDelayed: false,
          currentPriceDelayWarning: null,
        });
        return persisted;
      },
    );

    const result = await persistFreshConstituentQuote({
      constituent,
      quote: makeQuote(),
      persistQuote,
    });

    expect(persistQuote).toHaveBeenCalledOnce();
    expect(result.warningMessage).toBeNull();
    expect(result.persisted).toEqual(persisted);

    const updated = applyPersistedQuoteSnapshot(constituent, persisted);
    expect(updated.currentPrice).toBe(111.23);
    expect(updated.currentPriceChange).toBe(1.2346);
    expect(updated.currentPriceChangePercent).toBe(1.9877);
    expect(updated.currentPriceAt).toBe('2026-08-17T10:11:12Z');
    expect(updated.trackingStatus).toBe('CatalogOnly');
  });

  it('persists delayed quotes so the delayed snapshot survives reloads', async () => {
    const constituent = makeConstituent();
    const persistQuote = vi.fn(async (_stockId: number, patch: UpdateStockQuoteRequest) => {
      expect(patch.currentPriceIsDelayed).toBe(true);
      expect(patch.currentPriceDelayWarning).toBe('Котировка задержана');
      return makePersisted({
        currentPrice: 120,
        currentPriceIsDelayed: true,
        currentPriceDelayWarning: 'Котировка задержана',
      });
    });

    const result = await persistFreshConstituentQuote({
      constituent,
      quote: makeQuote({
        isStale: true,
        delayWarning: 'Котировка задержана',
        currentPriceEur: 120,
      }),
      persistQuote,
    });

    expect(persistQuote).toHaveBeenCalledOnce();
    expect(result.warningMessage).toBeNull();
    expect(result.persisted?.currentPriceIsDelayed).toBe(true);

    const updated = applyPersistedQuoteSnapshot(constituent, result.persisted!);
    expect(updated.currentPrice).toBe(120);
    expect(updated.currentPriceIsDelayed).toBe(true);
    expect(updated.currentPriceDelayWarning).toBe('Котировка задержана');
  });

  it('does not persist when EUR conversion is unavailable and returns a Russian warning', async () => {
    const constituent = makeConstituent({ ticker: 'VOD' });
    const persistQuote = vi.fn();

    const result = await persistFreshConstituentQuote({
      constituent,
      quote: makeQuote({
        currentPriceEur: null,
        changeEur: null,
        conversionWarning: null,
        currency: 'GBp',
        normalizedQuoteCurrency: 'GBP',
      }),
      persistQuote,
    });

    expect(persistQuote).not.toHaveBeenCalled();
    expect(result.persisted).toBeNull();
    expect(result.warningMessage).toContain(QUOTE_NO_EUR_MESSAGE);
    expect(result.warningMessage).toContain('VOD');
  });

  it('uses backend conversion warning verbatim when EUR conversion is unavailable', () => {
    const message = getNoEurQuoteMessage(
      'NESN',
      makeQuote({
        currentPriceEur: null,
        conversionWarning: 'Конвертация в EUR временно недоступна',
      }),
    );

    expect(message).toBe('Конвертация в EUR временно недоступна');
  });

  it('prevents duplicate/concurrent refresh clicks per stock id', () => {
    const inFlight = new Set<number>();

    expect(beginConstituentQuoteRefresh(inFlight, 7)).toBe(true);
    expect(beginConstituentQuoteRefresh(inFlight, 7)).toBe(false);

    finishConstituentQuoteRefresh(inFlight, 7);
    expect(beginConstituentQuoteRefresh(inFlight, 7)).toBe(true);
  });

  it('preserves null change and invalid timestamp semantics from the persisted snapshot', () => {
    const constituent = makeConstituent();
    const updated = applyPersistedQuoteSnapshot(
      constituent,
      makePersisted({
        currentPrice: 100.01,
        currentPriceChange: null,
        currentPriceChangePercent: null,
        currentPriceAt: null,
        currentPriceIsDelayed: true,
        currentPriceDelayWarning: 'Котировка задержана',
      }),
    );

    expect(updated.currentPrice).toBe(100.01);
    expect(updated.currentPriceChange).toBeNull();
    expect(updated.currentPriceChangePercent).toBeNull();
    expect(updated.currentPriceAt).toBeNull();
    expect(updated.currentPriceIsDelayed).toBe(true);
    expect(updated.currentPriceDelayWarning).toBe('Котировка задержана');
  });
});
