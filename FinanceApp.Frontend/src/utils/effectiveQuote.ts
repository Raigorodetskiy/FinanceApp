import type { Stock, StockExchange } from '../types';

/**
 * Maximum age (in milliseconds) for a quote to be considered fresh.
 * A quote is fresh when its `currentPriceAt` is within this window relative to
 * the current moment (`Date.now()` at evaluation time).
 */
export const FRESH_QUOTE_WINDOW_MS = 10 * 60 * 1000; // 10 minutes

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
 * Builds the unique set of stocks whose quotes should be refreshed for a
 * portfolio: each portfolio stock plus already-loaded equivalent stocks from
 * other exchanges, reusing the same strict matching rules as effective quotes.
 */
export const buildRefreshStockSet = (
  portfolioStocks: readonly Stock[],
  allStocks: readonly Stock[],
): Stock[] => {
  const refreshStocks: Stock[] = [];
  const seenIds = new Set<number>();

  const addCandidate = (candidate: Stock) => {
    if (!candidate.ticker?.trim()) return;

    const matchesPortfolio = portfolioStocks.some(
      (portfolioStock) =>
        candidate.id === portfolioStock.id || stocksMatch(candidate, portfolioStock),
    );

    if (!matchesPortfolio || seenIds.has(candidate.id)) return;

    seenIds.add(candidate.id);
    refreshStocks.push(candidate);
  };

  for (const candidate of portfolioStocks) {
    addCandidate(candidate);
  }
  for (const candidate of allStocks) {
    addCandidate(candidate);
  }

  return refreshStocks;
};

/**
 * Returns true when the stored stock price is not fresh.
 * A price is fresh only when `currentPriceAt` is present, parseable, and within
 * the last `FRESH_QUOTE_WINDOW_MS` (10 minutes) relative to `now`.
 * Timestamps in the future are also considered not fresh.
 */
export const isStockPriceStale = (stock: Stock, now: number = Date.now()): boolean => {
  if (!stock.currentPriceAt) return true;
  const ts = Date.parse(stock.currentPriceAt);
  if (!isFinite(ts)) return true;
  const age = now - ts;
  return age < 0 || age > FRESH_QUOTE_WINDOW_MS;
};

/**
 * Resolves the most appropriate current price for a portfolio position.
 *
 * Algorithm:
 * 1. Build candidates from the primary stock and all equivalent stocks (matched by
 *    `stocksMatch`) that have a fresh quote (within the last 10 minutes).
 * 2. Among all fresh candidates, select the one with the most recent `currentPriceAt`.
 * 3. Tie-break: primary stock wins; otherwise the stock with the lower `id` wins.
 * 4. If no fresh candidate exists (including the primary), fall back to the primary's
 *    stored price without changing `sourceExchange`/`sourceStockId`.
 * 5. The alternative-exchange label is shown only when a different Stock record is used.
 *
 * Only already-EUR-normalised price fields (`currentPrice`, `currentPriceChange`,
 * `currentPriceChangePercent`) are used, so no currency conversion is needed here.
 */
export const resolveEffectiveQuote = (
  primary: Stock,
  allStocks: Stock[],
  now: number = Date.now(),
): EffectiveQuote => {
  // Build fresh candidates: primary (if fresh) + matching stocks from other exchanges.
  const freshCandidates = allStocks.filter(
    (s) =>
      (s.id === primary.id || stocksMatch(s, primary)) && !isStockPriceStale(s, now),
  );

  if (freshCandidates.length === 0) {
    // No fresh quote available – keep primary's stored price.
    return {
      currentPrice: primary.currentPrice,
      currentPriceChange: primary.currentPriceChange ?? null,
      currentPriceChangePercent: primary.currentPriceChangePercent ?? null,
      currentPriceAt: primary.currentPriceAt ?? null,
      sourceExchange: null,
      sourceStockId: null,
    };
  }

  // Select the candidate with the most recent timestamp.
  // Tie-break: primary stock wins; otherwise lower id wins.
  const best = freshCandidates.reduce((prev, curr) => {
    const prevTs = prev.currentPriceAt ? Date.parse(prev.currentPriceAt) : -Infinity;
    const currTs = curr.currentPriceAt ? Date.parse(curr.currentPriceAt) : -Infinity;
    if (currTs > prevTs) return curr;
    if (currTs < prevTs) return prev;
    // Equal timestamps: primary wins; otherwise lower id wins.
    if (prev.id === primary.id) return prev;
    if (curr.id === primary.id) return curr;
    return curr.id < prev.id ? curr : prev;
  });

  if (best.id === primary.id) {
    return {
      currentPrice: best.currentPrice,
      currentPriceChange: best.currentPriceChange ?? null,
      currentPriceChangePercent: best.currentPriceChangePercent ?? null,
      currentPriceAt: best.currentPriceAt ?? null,
      sourceExchange: null,
      sourceStockId: null,
    };
  }

  return {
    currentPrice: best.currentPrice,
    currentPriceChange: best.currentPriceChange ?? null,
    currentPriceChangePercent: best.currentPriceChangePercent ?? null,
    currentPriceAt: best.currentPriceAt ?? null,
    sourceExchange: best.exchange,
    sourceStockId: best.id,
  };
};
