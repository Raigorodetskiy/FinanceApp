import type { Stock, StockExchange } from '../types';

/** Price age threshold matching StockQuoteResponse.isStale (>24 h). */
const STALE_THRESHOLD_MS = 24 * 60 * 60 * 1000;

export interface EffectiveQuote {
  currentPrice: number;
  currentPriceChange: number | null;
  currentPriceChangePercent: number | null;
  currentPriceAt: string | null;
  /**
   * The exchange of the stock whose price is being used.
   * `null` means the primary stock's own price is used.
   */
  sourceExchange: StockExchange | null;
  /**
   * The id of the stock whose price is being used.
   * `null` means the primary stock itself.
   */
  sourceStockId: number | null;
}

/**
 * Normalises a stock name for comparison: trim + lower-case.
 * Returns an empty string for null/undefined so empty values never match.
 */
export const normalizeStockName = (name: string | null | undefined): string =>
  (name ?? '').trim().toLowerCase();

/**
 * Returns true when two stocks represent the same company.
 * Matching rules (in order of preference):
 *   1. Both have a non-empty CommonName and they are equal (case/space-normalised).
 *   2. Both have a non-empty Name and they are exactly equal (case/space-normalised).
 * Substring / fuzzy matching is intentionally excluded to avoid false positives.
 */
export const stocksMatch = (a: Stock, b: Stock): boolean => {
  const aCN = normalizeStockName(a.commonName);
  const bCN = normalizeStockName(b.commonName);
  if (aCN && bCN && aCN === bCN) return true;

  const aName = normalizeStockName(a.name);
  const bName = normalizeStockName(b.name);
  if (aName && bName && aName === bName) return true;

  return false;
};

/**
 * Returns true when the stored stock price is considered stale.
 * A price is stale when `currentPriceAt` is absent, unparseable, or older than
 * 24 hours – mirroring the `isStale` flag on `StockQuoteResponse`.
 */
export const isStockPriceStale = (stock: Stock, now: number = Date.now()): boolean => {
  if (!stock.currentPriceAt) return true;
  const ts = Date.parse(stock.currentPriceAt);
  if (!isFinite(ts)) return true;
  return now - ts > STALE_THRESHOLD_MS;
};

/**
 * Resolves the most appropriate current price for a portfolio position.
 *
 * - If the primary stock's price is **not** stale, returns the primary's own price.
 * - If the primary's price **is** stale, finds an equivalent stock (matched by
 *   `stocksMatch`) on any other exchange whose price is not stale, and returns
 *   the one with the most recent `currentPriceAt`.
 * - Tie-break: lower stock `id` wins (deterministic).
 * - If no fresh alternative exists, falls back to the primary's price.
 *
 * Only already-EUR-normalised price fields (`currentPrice`, `currentPriceChange`,
 * `currentPriceChangePercent`) are used, so no currency conversion is needed here.
 */
export const resolveEffectiveQuote = (
  primary: Stock,
  allStocks: Stock[],
  now: number = Date.now(),
): EffectiveQuote => {
  if (!isStockPriceStale(primary, now)) {
    return {
      currentPrice: primary.currentPrice,
      currentPriceChange: primary.currentPriceChange ?? null,
      currentPriceChangePercent: primary.currentPriceChangePercent ?? null,
      currentPriceAt: primary.currentPriceAt ?? null,
      sourceExchange: null,
      sourceStockId: null,
    };
  }

  // Candidates: other stocks that match the primary and have a fresh price.
  const candidates = allStocks.filter(
    (s) => s.id !== primary.id && stocksMatch(s, primary) && !isStockPriceStale(s, now),
  );

  if (candidates.length === 0) {
    return {
      currentPrice: primary.currentPrice,
      currentPriceChange: primary.currentPriceChange ?? null,
      currentPriceChangePercent: primary.currentPriceChangePercent ?? null,
      currentPriceAt: primary.currentPriceAt ?? null,
      sourceExchange: null,
      sourceStockId: null,
    };
  }

  // Select the candidate with the most recent timestamp; tie-break by lower id.
  const best = candidates.reduce((prev, curr) => {
    const prevTs = prev.currentPriceAt ? Date.parse(prev.currentPriceAt) : -Infinity;
    const currTs = curr.currentPriceAt ? Date.parse(curr.currentPriceAt) : -Infinity;
    if (currTs > prevTs) return curr;
    if (currTs < prevTs) return prev;
    return curr.id < prev.id ? curr : prev;
  });

  return {
    currentPrice: best.currentPrice,
    currentPriceChange: best.currentPriceChange ?? null,
    currentPriceChangePercent: best.currentPriceChangePercent ?? null,
    currentPriceAt: best.currentPriceAt ?? null,
    sourceExchange: best.exchange,
    sourceStockId: best.id,
  };
};
