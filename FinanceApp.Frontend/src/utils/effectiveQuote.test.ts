import { describe, expect, it } from 'vitest';
import type { PortfolioItem, Stock } from '../types';
import {
  buildRefreshStockSet,
  FRESH_QUOTE_WINDOW_MS,
  isStockPriceStale,
  normalizeStockName,
  parseUtcTimestamp,
  resolveEffectiveQuote,
  stocksMatch,
} from './effectiveQuote';

// ── Helpers ──────────────────────────────────────────────────────────────────

const NOW = new Date('2026-08-13T12:00:00Z').getTime();

/** 2 minutes ago – fresh (within 10-minute window). */
const FRESH_2M = new Date(NOW - 2 * 60 * 1000).toISOString();
/** 7 minutes ago – fresh (within 10-minute window). */
const FRESH_7M = new Date(NOW - 7 * 60 * 1000).toISOString();
/** 8 minutes ago – fresh (within 10-minute window). */
const FRESH_8M = new Date(NOW - 8 * 60 * 1000).toISOString();
/** Exactly 10 minutes ago – boundary: still fresh. */
const FRESH_10M = new Date(NOW - 10 * 60 * 1000).toISOString();
/** 11 minutes ago – stale (outside 10-minute window). */
const STALE_11M = new Date(NOW - 11 * 60 * 1000).toISOString();
/** ~27 hours ago – clearly stale. */
const STALE_27H = new Date(NOW - 27 * 60 * 60 * 1000).toISOString();
/** 1 minute in the future – not fresh (future timestamp). */
const FUTURE_1M = new Date(NOW + 1 * 60 * 1000).toISOString();

/** Shorthand aliases used by legacy tests. */
const FRESH = FRESH_2M;
const STALE = STALE_27H;

const makeStock = (overrides: Partial<Stock> & Pick<Stock, 'id'>): Stock => ({
  ticker: `T${overrides.id}`,
  name: `Stock ${overrides.id}`,
  commonName: '',
  exchange: 'NYSE',
  currentPrice: 100,
  currentPriceChange: null,
  currentPriceChangePercent: null,
  currentPriceAt: FRESH,
  updatedAt: FRESH,
  ...overrides,
});

const makePortfolioItem = (
  stock: Stock,
  overrides: Partial<PortfolioItem> = {},
): PortfolioItem => ({
  id: overrides.id ?? stock.id,
  portfolioId: overrides.portfolioId ?? 1,
  stockId: overrides.stockId ?? stock.id,
  stock,
  quantity: overrides.quantity ?? 1,
  buyPrice: overrides.buyPrice ?? 100,
  boughtAt: overrides.boughtAt ?? FRESH,
});

describe('buildRefreshStockSet', () => {
  it('includes the original portfolio stock', () => {
    const primary = makeStock({ id: 1, ticker: 'SGA1', commonName: 'Seagate' });
    expect(buildRefreshStockSet([primary], [])).toEqual([primary]);
  });

  it('includes a matching alternative by CommonName', () => {
    const primary = makeStock({ id: 1, ticker: 'SGA1', name: 'Seagate Technology Holdings plc', commonName: 'Seagate' });
    const alternative = makeStock({ id: 2, ticker: 'STX', name: 'Different Exchange Name', commonName: 'Seagate', exchange: 'Frankfurt' });
    expect(buildRefreshStockSet([primary], [alternative])).toEqual([primary, alternative]);
  });

  it('includes a fallback match by exact normalized Name', () => {
    const primary = makeStock({ id: 1, ticker: 'AAPL', name: ' Apple Inc ', commonName: '' });
    const alternative = makeStock({ id: 2, ticker: 'APC', name: 'apple inc', commonName: '', exchange: 'Frankfurt' });
    expect(buildRefreshStockSet([primary], [alternative])).toEqual([primary, alternative]);
  });

  it('excludes unrelated stocks', () => {
    const primary = makeStock({ id: 1, ticker: 'SGA1', commonName: 'Seagate' });
    const unrelated = makeStock({ id: 2, ticker: 'NVDA', commonName: 'Nvidia' });
    expect(buildRefreshStockSet([primary], [unrelated])).toEqual([primary]);
  });

  it('deduplicates duplicate stock ids', () => {
    const primary = makeStock({ id: 1, ticker: 'SGA1', commonName: 'Seagate' });
    const duplicate = makeStock({ id: 1, ticker: 'SGA1', commonName: 'Seagate', currentPrice: 999 });
    const alternative = makeStock({ id: 2, ticker: 'STX', commonName: 'Seagate', exchange: 'Frankfurt' });
    expect(buildRefreshStockSet([primary], [duplicate, alternative, alternative])).toEqual([
      primary,
      alternative,
    ]);
  });

  it('excludes stocks with an empty ticker', () => {
    const primary = makeStock({ id: 1, ticker: 'SGA1', commonName: 'Seagate' });
    const emptyTickerPrimary = makeStock({ id: 2, ticker: '   ', commonName: 'Seagate' });
    const emptyTickerAlternative = makeStock({ id: 3, ticker: '', commonName: 'Seagate' });
    expect(
      buildRefreshStockSet([primary, emptyTickerPrimary], [emptyTickerAlternative]),
    ).toEqual([primary]);
  });
});

// ── 1. Primary fresh quote stays selected ────────────────────────────────────

describe('Req 1 – fresh primary quote is used as-is', () => {
  it('returns primary price when primary is not stale', () => {
    const primary = makeStock({ id: 1, currentPrice: 200, currentPriceAt: FRESH });
    const alt = makeStock({ id: 2, currentPrice: 999, currentPriceAt: FRESH, name: 'Stock 1', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);
    expect(eq.currentPrice).toBe(200);
    expect(eq.sourceStockId).toBeNull();
    expect(eq.sourceExchange).toBeNull();
  });
});

// ── 2. Stale primary replaced by fresh alt via CommonName ────────────────────

describe('Req 2 – stale primary replaced via CommonName', () => {
  it('uses fresh alternative matched by commonName', () => {
    const primary = makeStock({ id: 1, currentPrice: 150, currentPriceAt: STALE, commonName: 'microsoft' });
    const alt = makeStock({ id: 2, currentPrice: 155, currentPriceAt: FRESH, commonName: 'microsoft', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);
    expect(eq.currentPrice).toBe(155);
    expect(eq.sourceStockId).toBe(2);
    expect(eq.sourceExchange).toBe('Frankfurt');
  });
});

// ── 3. Fallback matching by Name ─────────────────────────────────────────────

describe('Req 3 – fallback matching by Name', () => {
  it('uses fresh alternative matched by name when commonName is empty', () => {
    const primary = makeStock({ id: 1, currentPrice: 100, currentPriceAt: STALE, name: 'Apple Inc', commonName: '' });
    const alt = makeStock({ id: 2, currentPrice: 102, currentPriceAt: FRESH, name: 'Apple Inc', commonName: '', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);
    expect(eq.currentPrice).toBe(102);
    expect(eq.sourceStockId).toBe(2);
  });
});

// ── 4. Case and whitespace normalisation ─────────────────────────────────────

describe('Req 4 – case and whitespace normalisation', () => {
  it('matches regardless of casing and surrounding spaces', () => {
    expect(normalizeStockName('  Microsoft  ')).toBe('microsoft');
    expect(normalizeStockName('APPLE INC')).toBe('apple inc');

    const primary = makeStock({ id: 1, currentPriceAt: STALE, name: '  Apple Inc  ', commonName: '  Apple  ' });
    const alt = makeStock({ id: 2, currentPrice: 120, currentPriceAt: FRESH, name: 'apple inc', commonName: 'apple', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);
    expect(eq.currentPrice).toBe(120);
  });
});

// ── 5. Empty names do not produce false matches ───────────────────────────────

describe('Req 5 – empty names never match', () => {
  it('does not match when both stocks have empty commonName and empty name', () => {
    const a = makeStock({ id: 1, name: '', commonName: '' });
    const b = makeStock({ id: 2, name: '', commonName: '' });
    expect(stocksMatch(a, b)).toBe(false);
  });

  it('does not match when one stock has empty commonName', () => {
    const a = makeStock({ id: 1, commonName: '', name: 'foo' });
    const b = makeStock({ id: 2, commonName: '', name: 'bar' });
    expect(stocksMatch(a, b)).toBe(false);
  });

  it('does not use an alternative when the primary name is empty', () => {
    const primary = makeStock({ id: 1, name: '', commonName: '', currentPriceAt: STALE });
    const alt = makeStock({ id: 2, name: '', commonName: '', currentPrice: 999, currentPriceAt: FRESH });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);
    expect(eq.sourceStockId).toBeNull();
    expect(eq.currentPrice).toBe(primary.currentPrice);
  });
});

// ── 6. Stale alternative is not selected ─────────────────────────────────────

describe('Req 6 – stale alternative is not selected', () => {
  it('falls back to primary when the only alternative is also stale', () => {
    const primary = makeStock({ id: 1, currentPrice: 100, currentPriceAt: STALE, commonName: 'nvidia' });
    const staleAlt = makeStock({ id: 2, currentPrice: 200, currentPriceAt: STALE, commonName: 'nvidia', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, staleAlt], NOW);
    expect(eq.currentPrice).toBe(100);
    expect(eq.sourceStockId).toBeNull();
  });

  it('checks isStockPriceStale correctly', () => {
    const fresh = makeStock({ id: 1, currentPriceAt: FRESH_2M });
    const stale = makeStock({ id: 2, currentPriceAt: STALE_27H });
    const noTs = makeStock({ id: 3, currentPriceAt: null });
    expect(isStockPriceStale(fresh, NOW)).toBe(false);
    expect(isStockPriceStale(stale, NOW)).toBe(true);
    expect(isStockPriceStale(noTs, NOW)).toBe(true);
  });
});

// ── 7. Most recent of multiple alternatives is chosen deterministically ───────

describe('Req 7 – most recent alternative chosen; deterministic tie-break', () => {
  it('picks the candidate with the most recent currentPriceAt', () => {
    const primary = makeStock({ id: 1, currentPrice: 100, currentPriceAt: STALE, commonName: 'tesla' });
    const altOlder = makeStock({ id: 2, currentPrice: 200, currentPriceAt: FRESH_7M, commonName: 'tesla', exchange: 'Frankfurt' });
    const altNewer = makeStock({ id: 3, currentPrice: 210, currentPriceAt: FRESH_2M, commonName: 'tesla', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, altOlder, altNewer], NOW);
    expect(eq.currentPrice).toBe(210);
    expect(eq.sourceStockId).toBe(3);
  });

  it('tie-breaks by lower stock id when timestamps are equal', () => {
    const primary = makeStock({ id: 1, currentPriceAt: STALE, commonName: 'tesla' });
    const altA = makeStock({ id: 3, currentPrice: 300, currentPriceAt: FRESH, commonName: 'tesla', exchange: 'Frankfurt' });
    const altB = makeStock({ id: 2, currentPrice: 200, currentPriceAt: FRESH, commonName: 'tesla', exchange: 'NYSE' });
    const eq = resolveEffectiveQuote(primary, [primary, altA, altB], NOW);
    expect(eq.sourceStockId).toBe(2); // lower id wins
  });
});

// ── 8. No alternative → primary price kept ───────────────────────────────────

describe('Req 8 – no alternative available, primary price used', () => {
  it('returns primary price when no other stock matches', () => {
    const primary = makeStock({ id: 1, currentPrice: 77, currentPriceAt: STALE, commonName: 'alone corp' });
    const unrelated = makeStock({ id: 2, currentPrice: 999, currentPriceAt: FRESH, commonName: 'other corp' });
    const eq = resolveEffectiveQuote(primary, [primary, unrelated], NOW);
    expect(eq.currentPrice).toBe(77);
    expect(eq.sourceStockId).toBeNull();
    expect(eq.sourceExchange).toBeNull();
  });
});

// ── 9. Effective price used consistently: value, P&L, totals ─────────────────

describe('Req 9 – effective price used consistently for value, P&L, and totals', () => {
  it('current value uses effective price', () => {
    const primary = makeStock({ id: 1, currentPrice: 100, currentPriceAt: STALE, commonName: 'acme' });
    const alt = makeStock({ id: 2, currentPrice: 120, currentPriceAt: FRESH, commonName: 'acme', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);
    const quantity = 5;
    expect(eq.currentPrice * quantity).toBe(600); // 120 × 5
  });

  it('P&L uses effective price', () => {
    const primary = makeStock({ id: 1, currentPrice: 100, currentPriceAt: STALE, commonName: 'acme' });
    const alt = makeStock({ id: 2, currentPrice: 120, currentPriceAt: FRESH, commonName: 'acme', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);
    const buyPrice = 90;
    const quantity = 5;
    const pnl = (eq.currentPrice - buyPrice) * quantity;
    expect(pnl).toBe(150); // (120 – 90) × 5
  });

  it('portfolio total sum uses effective prices across all items', () => {
    const primary1 = makeStock({ id: 1, currentPrice: 100, currentPriceAt: STALE, commonName: 'acme' });
    const alt1 = makeStock({ id: 2, currentPrice: 120, currentPriceAt: FRESH, commonName: 'acme', exchange: 'Frankfurt' });
    const primary2 = makeStock({ id: 3, currentPrice: 50, currentPriceAt: FRESH, commonName: 'widgetco' });
    const allStocks = [primary1, alt1, primary2];

    const eq1 = resolveEffectiveQuote(primary1, allStocks, NOW);
    const eq2 = resolveEffectiveQuote(primary2, allStocks, NOW);
    const total = eq1.currentPrice * 2 + eq2.currentPrice * 4;
    expect(total).toBe(440); // 120*2 + 50*4
  });
});

// ── 10. Original position identity is not changed ────────────────────────────

describe('Req 10 – original position identity preserved', () => {
  it('source stock fields (id, ticker, exchange, name) remain unchanged', () => {
    const primary = makeStock({ id: 1, ticker: 'MSFT', name: 'Microsoft', exchange: 'NYSE', currentPriceAt: STALE, commonName: 'microsoft' });
    const alt = makeStock({ id: 2, ticker: 'MSF', name: 'Microsoft', currentPrice: 300, currentPriceAt: FRESH, commonName: 'microsoft', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);

    // Effective price comes from alt
    expect(eq.currentPrice).toBe(300);
    expect(eq.sourceStockId).toBe(2);
    expect(eq.sourceExchange).toBe('Frankfurt');

    // Primary identity unchanged
    expect(primary.id).toBe(1);
    expect(primary.ticker).toBe('MSFT');
    expect(primary.exchange).toBe('NYSE');
    expect(primary.name).toBe('Microsoft');
  });

  it('resolveEffectiveQuote does not mutate the primary stock', () => {
    const primary = makeStock({ id: 1, currentPrice: 100, currentPriceAt: STALE, commonName: 'acme' });
    const alt = makeStock({ id: 2, currentPrice: 200, currentPriceAt: FRESH, commonName: 'acme', exchange: 'Frankfurt' });
    resolveEffectiveQuote(primary, [primary, alt], NOW);
    expect(primary.currentPrice).toBe(100);
  });

  it('portfolio item keeps its original stock identity after applying an alternative effective quote', () => {
    const primary = makeStock({ id: 1, ticker: 'STX', name: 'Seagate Technology Holdings plc', currentPrice: 756, currentPriceAt: STALE, commonName: 'Seagate' });
    const alt = makeStock({ id: 2, ticker: 'SEG', name: 'Different Exchange Name', currentPrice: 812, currentPriceAt: FRESH, commonName: 'Seagate', exchange: 'Frankfurt' });
    const item = makePortfolioItem(primary, { quantity: 3, buyPrice: 700 });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);

    const effectiveItem = {
      ...item,
      stock: {
        ...item.stock,
        currentPrice: eq.currentPrice,
        currentPriceChange: eq.currentPriceChange,
        currentPriceChangePercent: eq.currentPriceChangePercent,
        currentPriceAt: eq.currentPriceAt,
      },
    };

    expect(effectiveItem.stock.currentPrice).toBe(812);
    expect(effectiveItem.stockId).toBe(primary.id);
    expect(effectiveItem.stock.id).toBe(primary.id);
    expect(effectiveItem.stock.ticker).toBe('STX');
    expect(effectiveItem.stock.exchange).toBe('NYSE');
    expect(item.stockId).toBe(primary.id);
  });
});

// ── 11. 10-minute freshness window specifics ──────────────────────────────────

describe('Req 11 – 10-minute freshness window', () => {
  it('FRESH_QUOTE_WINDOW_MS equals 10 minutes', () => {
    expect(FRESH_QUOTE_WINDOW_MS).toBe(10 * 60 * 1000);
  });

  it('primary 8 min ago, alternative 1 min ago → alternative chosen', () => {
    const alt1M = new Date(NOW - 1 * 60 * 1000).toISOString();
    const primary = makeStock({ id: 1, currentPrice: 100, currentPriceAt: FRESH_8M, commonName: 'acme' });
    const alt = makeStock({ id: 2, currentPrice: 110, currentPriceAt: alt1M, commonName: 'acme', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);
    expect(eq.currentPrice).toBe(110);
    expect(eq.sourceStockId).toBe(2);
  });

  it('primary 2 min ago, alternative 7 min ago → primary chosen', () => {
    const primary = makeStock({ id: 1, currentPrice: 100, currentPriceAt: FRESH_2M, commonName: 'acme' });
    const alt = makeStock({ id: 2, currentPrice: 110, currentPriceAt: FRESH_7M, commonName: 'acme', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);
    expect(eq.currentPrice).toBe(100);
    expect(eq.sourceStockId).toBeNull();
  });

  it('alternative 11 min ago → not selected, fallback to primary stored price', () => {
    const primary = makeStock({ id: 1, currentPrice: 50, currentPriceAt: STALE, commonName: 'acme' });
    const alt = makeStock({ id: 2, currentPrice: 110, currentPriceAt: STALE_11M, commonName: 'acme', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);
    expect(eq.currentPrice).toBe(50);
    expect(eq.sourceStockId).toBeNull();
  });

  it('all candidates older than 10 min → fallback to primary stored price', () => {
    const primary = makeStock({ id: 1, currentPrice: 50, currentPriceAt: STALE_11M, commonName: 'acme' });
    const alt = makeStock({ id: 2, currentPrice: 110, currentPriceAt: STALE_27H, commonName: 'acme', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);
    expect(eq.currentPrice).toBe(50);
    expect(eq.sourceStockId).toBeNull();
  });

  it('timestamp exactly 10 minutes ago → considered fresh', () => {
    const primary = makeStock({ id: 1, currentPrice: 99, currentPriceAt: FRESH_10M, commonName: 'acme' });
    expect(isStockPriceStale(primary, NOW)).toBe(false);
    const eq = resolveEffectiveQuote(primary, [primary], NOW);
    expect(eq.currentPrice).toBe(99);
    expect(eq.sourceStockId).toBeNull();
  });

  it('timestamp from the future → not fresh', () => {
    const primary = makeStock({ id: 1, currentPrice: 99, currentPriceAt: FUTURE_1M });
    expect(isStockPriceStale(primary, NOW)).toBe(true);
  });

  it('same timestamp for primary and alternative → primary chosen', () => {
    const primary = makeStock({ id: 1, currentPrice: 100, currentPriceAt: FRESH_2M, commonName: 'acme' });
    const alt = makeStock({ id: 2, currentPrice: 110, currentPriceAt: FRESH_2M, commonName: 'acme', exchange: 'Frankfurt' });
    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);
    expect(eq.currentPrice).toBe(100);
    expect(eq.sourceStockId).toBeNull();
  });

  it('same timestamp for two alternatives with stale primary → lower id wins', () => {
    const primary = makeStock({ id: 5, currentPrice: 50, currentPriceAt: STALE, commonName: 'acme' });
    const altA = makeStock({ id: 3, currentPrice: 200, currentPriceAt: FRESH_2M, commonName: 'acme', exchange: 'Frankfurt' });
    const altB = makeStock({ id: 7, currentPrice: 300, currentPriceAt: FRESH_2M, commonName: 'acme', exchange: 'LSE' });
    const eq = resolveEffectiveQuote(primary, [primary, altA, altB], NOW);
    expect(eq.sourceStockId).toBe(3); // lower id wins
    expect(eq.currentPrice).toBe(200);
  });
});

// ── 12. parseUtcTimestamp – UTC parsing requirements ─────────────────────────

describe('Req 12 – parseUtcTimestamp', () => {
  // Req 12.1 – no-timezone string treated as UTC
  it('ISO string without timezone is treated as UTC', () => {
    const ts = parseUtcTimestamp('2026-08-13T20:00:00');
    expect(ts).toBe(Date.parse('2026-08-13T20:00:00Z'));
  });

  // Req 12.2 – string with Z is unchanged
  it('ISO string with Z is parsed without modification', () => {
    const ts = parseUtcTimestamp('2026-08-13T20:00:00Z');
    expect(ts).toBe(Date.parse('2026-08-13T20:00:00Z'));
  });

  // Req 12.3 – string with +02:00 is correctly converted
  it('ISO string with +02:00 offset maps to correct UTC epoch', () => {
    const ts = parseUtcTimestamp('2026-08-13T22:00:00+02:00');
    expect(ts).toBe(Date.parse('2026-08-13T20:00:00Z'));
  });

  // Req 12.4 – invalid / null / undefined return NaN
  it('null returns NaN', () => {
    expect(parseUtcTimestamp(null)).toBeNaN();
  });

  it('undefined returns NaN', () => {
    expect(parseUtcTimestamp(undefined)).toBeNaN();
  });

  it('empty string returns NaN', () => {
    expect(parseUtcTimestamp('')).toBeNaN();
  });

  it('non-date string returns NaN', () => {
    expect(parseUtcTimestamp('not-a-date')).toBeNaN();
  });

  // Req 12.5 – future timestamp is rejected by isStockPriceStale
  it('future timestamp is stale', () => {
    const futureTs = new Date(NOW + 5 * 60 * 1000).toISOString();
    const stock = makeStock({ id: 1, currentPriceAt: futureTs });
    expect(isStockPriceStale(stock, NOW)).toBe(true);
  });

  // Req 12.6 – exactly 10 minutes is fresh
  it('timestamp exactly 10 minutes ago is fresh', () => {
    const exactlyTenMin = new Date(NOW - FRESH_QUOTE_WINDOW_MS).toISOString();
    const stock = makeStock({ id: 1, currentPriceAt: exactlyTenMin });
    expect(isStockPriceStale(stock, NOW)).toBe(false);
  });

  // Req 12.7 – 10 minutes + 1 ms is stale
  it('timestamp 10 minutes and 1 ms ago is stale', () => {
    const justOverTenMin = new Date(NOW - FRESH_QUOTE_WINDOW_MS - 1).toISOString();
    const stock = makeStock({ id: 1, currentPriceAt: justOverTenMin });
    expect(isStockPriceStale(stock, NOW)).toBe(true);
  });
});

// ── 13. Seagate fixture (UTC parsing + Seagate scenario) ─────────────────────

describe('Req 13 – Seagate fixture', () => {
  // Observed data from the real API:
  //   Frankfurt, Stock 8:  currentPriceAt = "2026-08-13T06:00:59"  (no Z)
  //   NYSE,      Stock 54: currentPriceAt = "2026-08-13T20:00:00"  (no Z)
  //   Server UTC at check: 2026-08-13T20:54:11Z
  const SEAGATE_NOW = Date.parse('2026-08-13T20:54:11Z');
  const FRANKFURT_TS = '2026-08-13T06:00:59';  // ~54 min before 20:00, but ~14h54m before now
  const NYSE_TS = '2026-08-13T20:00:00';        // 54 min before SEAGATE_NOW

  // Req 13.1 – both timestamps treated as UTC
  it('Frankfurt timestamp (no Z) is parsed as UTC', () => {
    const ts = parseUtcTimestamp(FRANKFURT_TS);
    expect(ts).toBe(Date.parse('2026-08-13T06:00:59Z'));
  });

  it('NYSE timestamp (no Z) is parsed as UTC', () => {
    const ts = parseUtcTimestamp(NYSE_TS);
    expect(ts).toBe(Date.parse('2026-08-13T20:00:00Z'));
  });

  // Req 13.2 – both are stale at SEAGATE_NOW (54+ min old each)
  it('Frankfurt quote is stale at SEAGATE_NOW', () => {
    const stock = makeStock({ id: 8, currentPriceAt: FRANKFURT_TS });
    expect(isStockPriceStale(stock, SEAGATE_NOW)).toBe(true);
  });

  it('NYSE quote is stale at SEAGATE_NOW (54 min > 10 min window)', () => {
    const stock = makeStock({ id: 54, currentPriceAt: NYSE_TS });
    expect(isStockPriceStale(stock, SEAGATE_NOW)).toBe(true);
  });

  // Req 13.3 – resolveEffectiveQuote falls back to primary, diagnostic explains
  it('resolveEffectiveQuote falls back to primary and diagnostic mentions fallback', () => {
    const frankfurt = makeStock({
      id: 8,
      ticker: '847',
      exchange: 'Frankfurt',
      currentPrice: 756,
      currentPriceAt: FRANKFURT_TS,
      commonName: 'Seagate Technology Holdings PLC',
      name: 'Seagate Technology Holdings PLC',
    });
    const nyse = makeStock({
      id: 54,
      ticker: 'STX',
      exchange: 'NYSE',
      currentPrice: 798.83,
      currentPriceAt: NYSE_TS,
      commonName: 'Seagate Technology Holdings PLC',
      name: 'Seagate Technology Holdings PLC',
    });

    const eq = resolveEffectiveQuote(frankfurt, [frankfurt, nyse], SEAGATE_NOW);

    // Both stale → fall back to primary stored price
    expect(eq.currentPrice).toBe(756);
    expect(eq.sourceStockId).toBeNull();
    expect(eq.sourceExchange).toBeNull();
    // Diagnostic should mention "fallback"
    expect(eq.diagnosticInfo).toContain('fallback');
    // Diagnostic should mention both stocks
    expect(eq.diagnosticInfo).toContain('Stock 8');
    expect(eq.diagnosticInfo).toContain('Stock 54');
    // Diagnostic should mention both are stale
    expect(eq.diagnosticInfo).toContain('stale');
  });

  // Req 13.4 – fresh NYSE quote (within 10 min) IS selected
  it('fresh NYSE quote (within 10 min) is selected as alternative', () => {
    const freshNow = Date.parse('2026-08-13T20:05:00Z'); // 5 min after 20:00:00
    const frankfurt = makeStock({
      id: 8,
      ticker: '847',
      exchange: 'Frankfurt',
      currentPrice: 756,
      currentPriceAt: FRANKFURT_TS, // stale at freshNow too (~14h)
      commonName: 'Seagate Technology Holdings PLC',
      name: 'Seagate Technology Holdings PLC',
    });
    const nyse = makeStock({
      id: 54,
      ticker: 'STX',
      exchange: 'NYSE',
      currentPrice: 798.83,
      currentPriceAt: NYSE_TS, // 5 min old at freshNow → fresh
      commonName: 'Seagate Technology Holdings PLC',
      name: 'Seagate Technology Holdings PLC',
    });

    const eq = resolveEffectiveQuote(frankfurt, [frankfurt, nyse], freshNow);

    expect(eq.currentPrice).toBe(798.83);
    expect(eq.sourceStockId).toBe(54);
    expect(eq.sourceExchange).toBe('NYSE');
    expect(eq.diagnosticInfo).toContain('Stock 54');
  });
});
