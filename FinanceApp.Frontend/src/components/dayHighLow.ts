/**
 * Day high / day low display logic for StockPriceChart.
 *
 * Priority:
 *   1. Live quote value (rawDayHigh / rawDayLow → normalised / EUR) when present.
 *   2. High / low of the latest **completed** daily (`1d`) candle from the
 *      already-loaded history response, when the quote value is unavailable
 *      (e.g. weekends or market-closed state).
 *
 * "Completed" candle rule (conservative, exchange-agnostic):
 *   A `1d` candle is considered completed when its UTC date is strictly earlier
 *   than today's UTC date.  On a Saturday this yields Friday's candle; on a
 *   Monday morning (before the session opens) it still yields Friday's candle.
 *   The current in-progress daily candle is never used.
 */

import type { StockHistoryPoint, StockQuoteResponse } from '../types';

// ── Font-size constants (exported so tests can assert them) ───────────────────

/** Font size for day high / day low values. Slightly smaller than current price. */
export const DAY_HIGH_LOW_VALUE_FONT_SIZE = 14;

/** Font size for the current price display. */
export const CURRENT_PRICE_FONT_SIZE = 16;

// ── Labels ────────────────────────────────────────────────────────────────────

/** Label used when high/low comes from the live/current-session quote. */
export const DAY_HIGH_LIVE_LABEL = 'Макс. за день';
/** Label used when high/low comes from the live/current-session quote. */
export const DAY_LOW_LIVE_LABEL = 'Мин. за день';

/** Label used when high/low is a fallback from the last completed session. */
export const DAY_HIGH_FALLBACK_LABEL = 'Макс. последней сессии';
/** Label used when high/low is a fallback from the last completed session. */
export const DAY_LOW_FALLBACK_LABEL = 'Мин. последней сессии';

/**
 * Combined heading for the compact min–max block when both values come from
 * the live / current-session quote.
 */
export const DAY_RANGE_LIVE_LABEL = 'Мин.–макс. за день';

/**
 * Combined heading for the compact min–max block when at least one value
 * comes from the latest completed historical session (weekend / closed-market
 * fallback).
 */
export const DAY_RANGE_FALLBACK_LABEL = 'Мин.–макс. последней сессии';

// ── Types ─────────────────────────────────────────────────────────────────────

export interface DayHighLowEntry {
  /** Numeric value in the display currency, or null when unavailable. */
  value: number | null;
  /** Raw value in the provider's original quote units (for unit-multiplier tooltip). Null when not applicable. */
  rawValue: number | null;
  /** UI label: "Макс. за день" or "Макс. последней сессии" (or the Low equivalents). */
  label: string;
  /**
   * ISO date string (YYYY-MM-DD) of the session the value belongs to.
   * Populated only for fallback (history) values; null for live quote values.
   */
  fallbackDate: string | null;
  /** True when the value originates from historical data rather than the live quote. */
  isFromHistory: boolean;
}

export interface DayHighLowDisplay {
  high: DayHighLowEntry;
  low: DayHighLowEntry;
}

// ── Internal helpers ──────────────────────────────────────────────────────────

/**
 * Returns the UTC date portion (YYYY-MM-DD) of an ISO timestamp string.
 * Used to compare candle dates against the current UTC date.
 */
const utcDateOf = (isoTimestamp: string): string => isoTimestamp.slice(0, 10);

/**
 * Finds the latest completed `1d` candle from history points.
 *
 * "Completed" means the candle's UTC date is strictly before `todayUtcDate`
 * (format `YYYY-MM-DD`).  This conservative rule ensures the in-progress
 * current daily candle is never returned.
 *
 * @param points     History points (any mix of intervals).
 * @param todayUtcDate  Today's date in `YYYY-MM-DD` format (UTC).
 */
export const getLatestCompletedDailyCandle = (
  points: StockHistoryPoint[],
  todayUtcDate: string,
): StockHistoryPoint | null => {
  let latest: StockHistoryPoint | null = null;

  for (const point of points) {
    if (point.interval !== '1d') {
      continue;
    }

    const candleDate = utcDateOf(point.timestamp);
    if (candleDate >= todayUtcDate) {
      // Candle is today or in the future – not yet completed.
      continue;
    }

    if (latest === null || candleDate > utcDateOf(latest.timestamp)) {
      latest = point;
    }
  }

  return latest;
};

// ── Public API ────────────────────────────────────────────────────────────────

/**
 * Computes what to display for day high and day low in the chart details area.
 *
 * High and low are resolved **independently**: the quote value is used whenever
 * it is non-null; the history fallback is used only when the quote value is absent.
 *
 * @param liveQuote            Current quote from the API (may be null).
 * @param historyPoints        History points already loaded by StockPriceChart (any range/interval).
 * @param historyHasEurConversion  Whether the history response includes EUR-converted values.
 * @param todayUtcDate         Today's UTC date in `YYYY-MM-DD` format.  Caller provides
 *                             this so that the function remains pure / testable.
 */
export const getDayHighLowDisplay = (
  liveQuote: StockQuoteResponse | null | undefined,
  historyPoints: StockHistoryPoint[],
  historyHasEurConversion: boolean,
  todayUtcDate: string,
): DayHighLowDisplay => {
  // Resolve the live quote values in the display currency.
  const liveHighDisplay = historyHasEurConversion
    ? (liveQuote?.dayHighEur ?? null)
    : (liveQuote?.normalizedDayHigh ?? liveQuote?.rawDayHigh ?? null);

  const liveLowDisplay = historyHasEurConversion
    ? (liveQuote?.dayLowEur ?? null)
    : (liveQuote?.normalizedDayLow ?? liveQuote?.rawDayLow ?? null);

  // Only look up the fallback candle when at least one of the values is missing.
  const needsFallback = liveHighDisplay === null || liveLowDisplay === null;
  const fallbackCandle = needsFallback
    ? getLatestCompletedDailyCandle(historyPoints, todayUtcDate)
    : null;

  const getFallbackDate = (candle: StockHistoryPoint | null): string | null =>
    candle ? utcDateOf(candle.timestamp) : null;

  const getFallbackHighValue = (candle: StockHistoryPoint | null): number | null => {
    if (candle === null) return null;
    if (historyHasEurConversion) return candle.highEur ?? null;
    return candle.highNormalized ?? candle.highRaw ?? null;
  };

  const getFallbackLowValue = (candle: StockHistoryPoint | null): number | null => {
    if (candle === null) return null;
    if (historyHasEurConversion) return candle.lowEur ?? null;
    return candle.lowNormalized ?? candle.lowRaw ?? null;
  };

  const high: DayHighLowEntry =
    liveHighDisplay !== null
      ? {
          value: liveHighDisplay,
          rawValue: liveQuote?.rawDayHigh ?? null,
          label: DAY_HIGH_LIVE_LABEL,
          fallbackDate: null,
          isFromHistory: false,
        }
      : {
          value: getFallbackHighValue(fallbackCandle),
          rawValue: null,
          label: DAY_HIGH_FALLBACK_LABEL,
          fallbackDate: getFallbackDate(fallbackCandle),
          isFromHistory: true,
        };

  const low: DayHighLowEntry =
    liveLowDisplay !== null
      ? {
          value: liveLowDisplay,
          rawValue: liveQuote?.rawDayLow ?? null,
          label: DAY_LOW_LIVE_LABEL,
          fallbackDate: null,
          isFromHistory: false,
        }
      : {
          value: getFallbackLowValue(fallbackCandle),
          rawValue: null,
          label: DAY_LOW_FALLBACK_LABEL,
          fallbackDate: getFallbackDate(fallbackCandle),
          isFromHistory: true,
        };

  return { high, low };
};

/**
 * Returns the combined heading label for the compact min–max block.
 *
 * Uses the live label when *neither* bound comes from historical fallback;
 * uses the fallback label as soon as at least one bound originates from the
 * latest completed historical session.
 */
export const getDayRangeLabel = (display: DayHighLowDisplay): string =>
  display.low.isFromHistory || display.high.isFromHistory
    ? DAY_RANGE_FALLBACK_LABEL
    : DAY_RANGE_LIVE_LABEL;
