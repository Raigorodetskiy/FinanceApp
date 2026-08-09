import type {
  StockHistoryPoint,
  StockHistoryRange,
  StockQuoteResponse,
} from '../types';

// ── Font-size constants (exported so tests can assert them) ───────────────────

/** Font size for compact range min/max values. Slightly smaller than current price. */
export const DAY_HIGH_LOW_VALUE_FONT_SIZE = 14;

/** Font size for the current price display. */
export const CURRENT_PRICE_FONT_SIZE = 16;

export const RANGE_MIN_MAX_LABELS: Record<StockHistoryRange, string> = {
  today: 'Мин.–макс. сегодня',
  '24h': 'Мин.–макс. за 24 ч.',
  '1w': 'Мин.–макс. за 1 нед.',
  '1m': 'Мин.–макс. за 1 мес.',
  '3m': 'Мин.–макс. за 3 мес.',
  '6m': 'Мин.–макс. за 6 мес.',
  '1y': 'Мин.–макс. за 1 год',
  '3y': 'Мин.–макс. за 3 года',
  '5y': 'Мин.–макс. за 5 лет',
};

// ── Types ─────────────────────────────────────────────────────────────────────

export interface DayHighLowEntry {
  /** Numeric value in the display currency, or null when unavailable. */
  value: number | null;
  /** Raw value in quote units for tooltip details. */
  rawValue: number | null;
  /** UTC timestamp of the candle/quote that produced the bound. */
  timestampUtc: string | null;
  /** True when value originates from the live quote. */
  isFromLiveQuote: boolean;
}

export interface DayHighLowDisplay {
  minimum: DayHighLowEntry;
  maximum: DayHighLowEntry;
}

const getUtcDate = (timestamp: string): string => {
  const parsed = Date.parse(timestamp);
  if (Number.isFinite(parsed)) {
    return new Date(parsed).toISOString().slice(0, 10);
  }

  return timestamp.slice(0, 10);
};
const isFiniteNumber = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value);

const pickHistoryBound = (
  point: StockHistoryPoint,
  bound: 'low' | 'high',
  historyHasEurConversion: boolean,
): number | null => {
  const eurValue = bound === 'low' ? point.lowEur : point.highEur;
  const normalizedValue = bound === 'low' ? point.lowNormalized : point.highNormalized;
  const rawValue = bound === 'low' ? point.lowRaw : point.highRaw;

  if (historyHasEurConversion) {
    return eurValue ?? normalizedValue ?? rawValue ?? null;
  }

  return normalizedValue ?? rawValue ?? null;
};

const pickLiveBound = (
  quote: StockQuoteResponse | null | undefined,
  bound: 'low' | 'high',
  historyHasEurConversion: boolean,
): number | null => {
  const eurValue = bound === 'low' ? quote?.dayLowEur : quote?.dayHighEur;
  const normalizedValue = bound === 'low' ? quote?.normalizedDayLow : quote?.normalizedDayHigh;
  const rawValue = bound === 'low' ? quote?.rawDayLow : quote?.rawDayHigh;

  if (historyHasEurConversion) {
    return eurValue ?? normalizedValue ?? rawValue ?? null;
  }

  return normalizedValue ?? rawValue ?? null;
};

const getLatestHistoryTimestampMsForDate = (
  points: StockHistoryPoint[],
  utcDate: string,
): number | null => {
  let latest: number | null = null;
  for (const point of points) {
    if (getUtcDate(point.timestamp) !== utcDate) {
      continue;
    }

    const timestampMs = Date.parse(point.timestamp);
    if (!Number.isFinite(timestampMs)) {
      continue;
    }

    latest = latest == null ? timestampMs : Math.max(latest, timestampMs);
  }
  return latest;
};

const createEmptyEntry = (): DayHighLowEntry => ({
  value: null,
  rawValue: null,
  timestampUtc: null,
  isFromLiveQuote: false,
});

export const getDayHighLowDisplay = (
  liveQuote: StockQuoteResponse | null | undefined,
  historyPoints: StockHistoryPoint[],
  historyHasEurConversion: boolean,
): DayHighLowDisplay => {
  let minimum = createEmptyEntry();
  let maximum = createEmptyEntry();

  for (const point of historyPoints) {
    const lowValue = pickHistoryBound(point, 'low', historyHasEurConversion);
    if (isFiniteNumber(lowValue) && (minimum.value == null || lowValue < minimum.value)) {
      minimum = {
        value: lowValue,
        rawValue: isFiniteNumber(point.lowRaw) ? point.lowRaw : null,
        timestampUtc: point.timestamp,
        isFromLiveQuote: false,
      };
    }

    const highValue = pickHistoryBound(point, 'high', historyHasEurConversion);
    if (isFiniteNumber(highValue) && (maximum.value == null || highValue > maximum.value)) {
      maximum = {
        value: highValue,
        rawValue: isFiniteNumber(point.highRaw) ? point.highRaw : null,
        timestampUtc: point.timestamp,
        isFromLiveQuote: false,
      };
    }
  }

  const liveTimestampUtc = liveQuote?.priceTimestampUtc;
  if (liveTimestampUtc != null) {
    const liveTimestampMs = Date.parse(liveTimestampUtc);
    if (Number.isFinite(liveTimestampMs)) {
      const liveDate = getUtcDate(liveTimestampUtc);
      const rangeContainsLiveDay = historyPoints.some((point) => getUtcDate(point.timestamp) === liveDate);
      if (rangeContainsLiveDay) {
        const latestHistoryOnLiveDateMs = getLatestHistoryTimestampMsForDate(historyPoints, liveDate);
        const liveIsFresherThanHistory =
          latestHistoryOnLiveDateMs == null || liveTimestampMs > latestHistoryOnLiveDateMs;

        if (liveIsFresherThanHistory) {
          const liveLowValue = pickLiveBound(liveQuote, 'low', historyHasEurConversion);
          if (isFiniteNumber(liveLowValue) && (minimum.value == null || liveLowValue < minimum.value)) {
            minimum = {
              value: liveLowValue,
              rawValue: isFiniteNumber(liveQuote?.rawDayLow) ? liveQuote.rawDayLow : null,
              timestampUtc: liveTimestampUtc,
              isFromLiveQuote: true,
            };
          }

          const liveHighValue = pickLiveBound(liveQuote, 'high', historyHasEurConversion);
          if (isFiniteNumber(liveHighValue) && (maximum.value == null || liveHighValue > maximum.value)) {
            maximum = {
              value: liveHighValue,
              rawValue: isFiniteNumber(liveQuote?.rawDayHigh) ? liveQuote.rawDayHigh : null,
              timestampUtc: liveTimestampUtc,
              isFromLiveQuote: true,
            };
          }
        }
      }
    }
  }

  return { minimum, maximum };
};

export const getDayRangeLabel = (range: StockHistoryRange): string => RANGE_MIN_MAX_LABELS[range];
