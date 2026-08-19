import dayjs from 'dayjs';
import utc from 'dayjs/plugin/utc';
import type { StockHistoryPoint, StockHistoryRange } from '../types';

dayjs.extend(utc);

const SHORT_INTRADAY_GAP_THRESHOLD_MS = 2 * 60 * 60 * 1000;
const MIN_GAP_MARKER_OFFSET_MS = 1;

/** Ranges that use daily (1d) candles from the provider. */
const DAILY_CANDLE_RANGES = new Set<StockHistoryRange>(['1m', '3m', '6m']);

const historyGapThresholdMsByRange: Partial<Record<StockHistoryRange, number>> = {
  '24h': SHORT_INTRADAY_GAP_THRESHOLD_MS,
  today: SHORT_INTRADAY_GAP_THRESHOLD_MS,
};

export type HistoryChartPoint = {
  timestamp: string;
  timestampMs: number;
  closeChart: number | null;
  rawClose: number;
  volumeChart: number | null;
  chartIndex?: number;
};

export const buildHistoryChartData = (
  historyData: StockHistoryPoint[],
  historyRange: StockHistoryRange,
): HistoryChartPoint[] => {
  const sortedPoints: HistoryChartPoint[] = historyData
    .map((point) => ({
      timestamp: point.timestamp,
      timestampMs: dayjs.utc(point.timestamp).valueOf(),
      closeChart: point.closeEur ?? point.closeNormalized,
      rawClose: point.closeRaw,
      volumeChart: point.volume,
    }))
    .sort((left, right) => left.timestampMs - right.timestampMs);

  if (historyRange === '1w') {
    return sortedPoints.map((pt, idx) => ({ ...pt, chartIndex: idx }));
  }

  const gapThresholdMs = historyGapThresholdMsByRange[historyRange];
  if (!gapThresholdMs || sortedPoints.length < 2) {
    return sortedPoints;
  }

  const pointsWithGaps: HistoryChartPoint[] = [sortedPoints[0]];
  let previousPoint = sortedPoints[0];
  for (let i = 1; i < sortedPoints.length; i += 1) {
    const currentPoint = sortedPoints[i];
    const gapMs = currentPoint.timestampMs - previousPoint.timestampMs;
    if (gapMs > gapThresholdMs) {
      const gapTimestampMs = previousPoint.timestampMs + MIN_GAP_MARKER_OFFSET_MS;
      pointsWithGaps.push({
        timestamp: new Date(gapTimestampMs).toISOString(),
        timestampMs: gapTimestampMs,
        closeChart: null,
        rawClose: previousPoint.rawClose,
        volumeChart: null,
      });
    }
    pointsWithGaps.push(currentPoint);
    previousPoint = currentPoint;
  }

  return pointsWithGaps;
};

/**
 * Appends a synthetic current-quote point to daily-candle chart data for 1m/3m/6m ranges.
 *
 * Conditions for appending:
 * - range uses daily candles (1m, 3m, 6m)
 * - quote is not stale
 * - quoteTimestampUtc is provided
 * - quote timestamp is strictly after the last existing point
 * - quote UTC date differs from the last point's UTC date (prevents duplicates when today's
 *   candle is already in the DB)
 */
export const appendCurrentQuotePoint = (
  points: HistoryChartPoint[],
  range: StockHistoryRange,
  quotePrice: number | null | undefined,
  quoteTimestampUtc: string | null | undefined,
  isStale: boolean,
): HistoryChartPoint[] => {
  if (!DAILY_CANDLE_RANGES.has(range)) return points;
  if (isStale) return points;
  if (quotePrice == null || quoteTimestampUtc == null) return points;

  const quoteMs = dayjs.utc(quoteTimestampUtc).valueOf();
  if (!isFinite(quoteMs)) return points;

  const lastRealPoint = points.length > 0 ? points[points.length - 1] : null;
  if (lastRealPoint == null) return points;

  if (quoteMs <= lastRealPoint.timestampMs) return points;

  const lastDate = dayjs.utc(lastRealPoint.timestampMs).format('YYYY-MM-DD');
  const quoteDate = dayjs.utc(quoteMs).format('YYYY-MM-DD');
  if (lastDate === quoteDate) return points;

  const syntheticPoint: HistoryChartPoint = {
    timestamp: quoteTimestampUtc,
    timestampMs: quoteMs,
    closeChart: quotePrice,
    rawClose: quotePrice,
    volumeChart: null,
  };

  return [...points, syntheticPoint];
};
