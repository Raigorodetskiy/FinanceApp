import type { Stock, StockExchange } from '../types';

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
  /**
   * Human-readable diagnostic summary explaining how the effective quote was
   * resolved. Includes each candidate's timestamp status and the selection reason.
   * Intended for tooltip display or `console.info` in development.
   */
  diagnosticInfo: string;
}

/**
 * Parses a quote timestamp string as a UTC epoch millisecond value.
 *
 * - ISO 8601 strings that already carry `Z` or an explicit UTC offset are
 *   parsed without modification.
 * - Strings that look like ISO 8601 but carry no timezone designator are
 *   treated as UTC (a trailing `Z` is appended). This covers MySQL `datetime`
 *   values that store the UTC clock but are serialized without a suffix.
 * - Empty, null, undefined, or unparseable values return `NaN`.
 */
export const parseUtcTimestamp = (value: string | null | undefined): number => {
  if (!value) return NaN;
  // Already has Z or an explicit UTC offset (+HH:MM, -HH:MM, +HHMM, etc.)
  const normalized = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value : `${value}Z`;
  return Date.parse(normalized);
};

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
 * Resolves the most appropriate current price for a portfolio position.
 *
 * Algorithm:
 * 1. Build candidates from the primary stock and all equivalent stocks (matched by
 *    `stocksMatch`).
 * 2. Keep only candidates with a parseable `currentPriceAt` that is not in the future.
 * 3. Among all valid candidates, select the one with the most recent `currentPriceAt`.
 * 4. Tie-break: primary stock wins; otherwise the stock with the lower `id` wins.
 * 5. If no valid candidate exists (including the primary), fall back to the primary's
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
  // Build candidates: primary + all matching stocks from any exchange.
  const candidates = [
    primary,
    ...allStocks.filter((stock) => stock.id !== primary.id && stocksMatch(stock, primary)),
  ];

  const candidateDiagnostics = candidates.map((stock) => {
    const ts = parseUtcTimestamp(stock.currentPriceAt);
    const parseValid = Number.isFinite(ts);
    const isFuture = parseValid && ts > now;
    const isValid = parseValid && !isFuture;
    const utc = parseValid ? new Date(ts).toISOString() : 'n/a';
    const ageMinutes = parseValid ? ((now - ts) / 60_000).toFixed(1) : 'n/a';
    const status = !stock.currentPriceAt
      ? 'invalid (no timestamp)'
      : !parseValid
        ? 'invalid (unparseable)'
        : isFuture
          ? `future (+${((ts - now) / 60_000).toFixed(1)} min)`
          : 'valid';

    return {
      stock,
      ts,
      isValid,
      line:
        `  Stock ${stock.id} (${stock.ticker ?? '?'} / ${stock.exchange}): ` +
        `raw="${stock.currentPriceAt ?? ''}" utc="${utc}" ageMin=${ageMinutes} status=${status}`,
    };
  });

  const candidateLines = candidateDiagnostics.map((entry) => entry.line);
  const validCandidates = candidateDiagnostics.filter((entry) => entry.isValid);

  if (validCandidates.length === 0) {
    // No valid quote available – keep primary's stored price.
    const diagnostic =
      `primary=${primary.id} (${primary.ticker ?? '?'} / ${primary.exchange}) now=${new Date(now).toISOString()}\n` +
      `candidates:\n${candidateLines.join('\n')}\n` +
      `result: fallback to primary stored price (no valid candidate timestamp)`;
    return {
      currentPrice: primary.currentPrice,
      currentPriceChange: primary.currentPriceChange ?? null,
      currentPriceChangePercent: primary.currentPriceChangePercent ?? null,
      currentPriceAt: primary.currentPriceAt ?? null,
      sourceExchange: null,
      sourceStockId: null,
      diagnosticInfo: diagnostic,
    };
  }

  // Select the valid candidate with the most recent timestamp.
  // Tie-break: primary stock wins; otherwise lower id wins.
  const best = validCandidates.reduce((prev, curr) => {
    if (curr.ts > prev.ts) return curr;
    if (curr.ts < prev.ts) return prev;
    // Equal timestamps: primary wins; otherwise lower id wins.
    if (prev.stock.id === primary.id) return prev;
    if (curr.stock.id === primary.id) return curr;
    return curr.stock.id < prev.stock.id ? curr : prev;
  });

  const selectionReason =
    best.stock.id === primary.id
      ? `selected newest valid primary (Stock ${best.stock.id})`
      : `selected newest valid alternative Stock ${best.stock.id} (${best.stock.ticker ?? '?'} / ${best.stock.exchange})`;

  const diagnostic =
    `primary=${primary.id} (${primary.ticker ?? '?'} / ${primary.exchange}) now=${new Date(now).toISOString()}\n` +
    `candidates:\n${candidateLines.join('\n')}\n` +
    `result: ${selectionReason}`;

  if (best.stock.id === primary.id) {
    return {
      currentPrice: best.stock.currentPrice,
      currentPriceChange: best.stock.currentPriceChange ?? null,
      currentPriceChangePercent: best.stock.currentPriceChangePercent ?? null,
      currentPriceAt: best.stock.currentPriceAt ?? null,
      sourceExchange: null,
      sourceStockId: null,
      diagnosticInfo: diagnostic,
    };
  }

  return {
    currentPrice: best.stock.currentPrice,
    currentPriceChange: best.stock.currentPriceChange ?? null,
    currentPriceChangePercent: best.stock.currentPriceChangePercent ?? null,
    currentPriceAt: best.stock.currentPriceAt ?? null,
    sourceExchange: best.stock.exchange,
    sourceStockId: best.stock.id,
    diagnosticInfo: diagnostic,
  };
};
