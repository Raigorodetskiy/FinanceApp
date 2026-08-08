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
