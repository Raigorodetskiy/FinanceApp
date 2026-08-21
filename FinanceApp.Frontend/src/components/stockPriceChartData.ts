import dayjs from 'dayjs';
import utc from 'dayjs/plugin/utc';
import type { StockHistoryPoint, StockHistoryRange } from '../types';

dayjs.extend(utc);

const SHORT_INTRADAY_GAP_THRESHOLD_MS = 2 * 60 * 60 * 1000;
const MIN_GAP_MARKER_OFFSET_MS = 1;

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
  isQuoteDerived?: boolean;
  chartIndex?: number;
};

export type CurrentQuoteOverlayPoint = {
  timestampUtc: string | null | undefined;
  closeChart: number | null | undefined;
  rawClose?: number | null | undefined;
  isStale?: boolean | null;
};

const DATE_ONLY_HISTORY_RANGE_SET = new Set<StockHistoryRange>(['5y', '3y', '1y', '6m', '3m', '1m']);
const SHORT_DAILY_HISTORY_RANGE_SET = new Set<StockHistoryRange>(['6m', '3m', '1m']);

const isFiniteNumber = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value);

export const usesUtcDateLabels = (historyRange: StockHistoryRange): boolean =>
  DATE_ONLY_HISTORY_RANGE_SET.has(historyRange);

export const formatHistoryTimestamp = (
  timestamp: string | number,
  historyRange: StockHistoryRange,
  format: string,
): string => {
  const parsed = dayjs.utc(timestamp);
  return usesUtcDateLabels(historyRange)
    ? parsed.format(format)
    : parsed.local().format(format);
};

const getEffectiveHistoryDateKey = (
  timestamp: string,
  historyRange: StockHistoryRange,
): string => formatHistoryTimestamp(timestamp, historyRange, 'YYYY-MM-DD');

export const buildHistoryChartData = (
  historyData: StockHistoryPoint[],
  historyRange: StockHistoryRange,
  currentQuoteOverlay?: CurrentQuoteOverlayPoint | null,
): HistoryChartPoint[] => {
  const sortedPoints: HistoryChartPoint[] = historyData
    .map((point) => ({
      timestamp: point.timestamp,
      timestampMs: dayjs.utc(point.timestamp).valueOf(),
      closeChart: point.closeEur ?? point.closeNormalized,
      rawClose: point.closeRaw,
      volumeChart: point.volume,
      isQuoteDerived: point.isQuoteDerived ?? false,
    }))
    .sort((left, right) => left.timestampMs - right.timestampMs);

  if (SHORT_DAILY_HISTORY_RANGE_SET.has(historyRange) && currentQuoteOverlay?.isStale !== true) {
    const overlayTimestamp = currentQuoteOverlay?.timestampUtc ?? null;
    const overlayClose = currentQuoteOverlay?.closeChart ?? null;
    const overlayTimestampMs = overlayTimestamp ? dayjs.utc(overlayTimestamp).valueOf() : Number.NaN;
    const latestPoint = sortedPoints[sortedPoints.length - 1];

    if (
      latestPoint != null
      && overlayTimestamp != null
      && Number.isFinite(overlayTimestampMs)
      && isFiniteNumber(overlayClose)
      && overlayTimestampMs > latestPoint.timestampMs
      && getEffectiveHistoryDateKey(overlayTimestamp, historyRange) !== getEffectiveHistoryDateKey(latestPoint.timestamp, historyRange)
    ) {
      sortedPoints.push({
        timestamp: overlayTimestamp,
        timestampMs: overlayTimestampMs,
        closeChart: overlayClose,
        rawClose: currentQuoteOverlay?.rawClose ?? overlayClose,
        volumeChart: null,
        isQuoteDerived: false,
      });
    }
  }

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
