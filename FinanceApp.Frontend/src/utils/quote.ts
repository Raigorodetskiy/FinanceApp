import type { StockQuoteResponse } from '../types';

/**
 * Returns true when a quote response is flagged as delayed/stale.
 * Covers both the boolean `isStale` flag and a non-empty `delayWarning` string
 * so the check remains correct when the backend adds more signal paths later.
 */
export const isQuoteDelayed = (quote: StockQuoteResponse | null | undefined): boolean =>
  !!quote?.isStale || !!(quote?.delayWarning);

/**
 * Returns true when the incoming quote timestamp is strictly newer than the
 * stored timestamp.  Both null/invalid cases are treated as "not newer" so we
 * never silently clobber a stored value with an unknown-age delayed quote.
 */
export const isQuoteNewerThanStored = (
  quoteTimestamp: string | null | undefined,
  storedTimestamp: string | null | undefined,
): boolean => {
  const quoteMs = quoteTimestamp ? Date.parse(quoteTimestamp) : NaN;
  const storedMs = storedTimestamp ? Date.parse(storedTimestamp) : NaN;
  // Only consider newer when we have a valid quote timestamp that is strictly
  // greater than the stored one.  If stored is NaN (never set) we allow it.
  if (!isFinite(quoteMs)) return false;
  if (!isFinite(storedMs)) return true;
  return quoteMs > storedMs;
};
