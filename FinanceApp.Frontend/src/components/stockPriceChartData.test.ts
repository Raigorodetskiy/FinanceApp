import { describe, expect, it } from 'vitest';
import { buildHistoryChartData, appendCurrentQuotePoint } from './stockPriceChartData';
import type { HistoryChartPoint } from './stockPriceChartData';

const makePoint = (timestamp: string, closeChart: number): HistoryChartPoint => ({
  timestamp,
  timestampMs: new Date(timestamp).getTime(),
  closeChart,
  rawClose: closeChart,
  volumeChart: 100,
});

describe('buildHistoryChartData', () => {
  it('keeps volume aligned with price points and inserts null gap markers for intraday gaps', () => {
    const data = buildHistoryChartData([
      {
        timestamp: '2026-08-08T10:00:00.000Z',
        interval: '10m',
        openRaw: 10,
        highRaw: 10,
        lowRaw: 10,
        closeRaw: 10,
        openNormalized: 10,
        highNormalized: 10,
        lowNormalized: 10,
        closeNormalized: 10,
        openEur: 10,
        highEur: 10,
        lowEur: 10,
        closeEur: 10,
        volume: 1000,
      },
      {
        timestamp: '2026-08-08T14:30:00.000Z',
        interval: '10m',
        openRaw: 12,
        highRaw: 12,
        lowRaw: 12,
        closeRaw: 12,
        openNormalized: 12,
        highNormalized: 12,
        lowNormalized: 12,
        closeNormalized: 12,
        openEur: 12,
        highEur: 12,
        lowEur: 12,
        closeEur: 12,
        volume: 2500,
      },
    ], '24h');

    expect(data).toHaveLength(3);
    expect(data[0]).toMatchObject({ closeChart: 10, volumeChart: 1000 });
    expect(data[1]).toMatchObject({ closeChart: null, volumeChart: null });
    expect(data[2]).toMatchObject({ closeChart: 12, volumeChart: 2500 });
  });

  it('adds stable chart indexes for 1w data without breaking timestamp ordering', () => {
    const data = buildHistoryChartData([
      {
        timestamp: '2026-08-08T12:00:00.000Z',
        interval: '1h',
        openRaw: 20,
        highRaw: 20,
        lowRaw: 20,
        closeRaw: 20,
        openNormalized: 20,
        highNormalized: 20,
        lowNormalized: 20,
        closeNormalized: 20,
        openEur: null,
        highEur: null,
        lowEur: null,
        closeEur: null,
        volume: 400,
      },
      {
        timestamp: '2026-08-08T10:00:00.000Z',
        interval: '1h',
        openRaw: 18,
        highRaw: 18,
        lowRaw: 18,
        closeRaw: 18,
        openNormalized: 18,
        highNormalized: 18,
        lowNormalized: 18,
        closeNormalized: 18,
        openEur: null,
        highEur: null,
        lowEur: null,
        closeEur: null,
        volume: 300,
      },
    ], '1w');

    expect(data.map((point) => point.chartIndex)).toEqual([0, 1]);
    expect(data.map((point) => point.timestamp)).toEqual([
      '2026-08-08T10:00:00.000Z',
      '2026-08-08T12:00:00.000Z',
    ]);
    expect(data.map((point) => point.volumeChart)).toEqual([300, 400]);
  });
});

describe('appendCurrentQuotePoint', () => {
  // Last history point is on Aug 18 (yesterday); quote is on Aug 19 (today) — should append.
  it('appends a current quote point for 1m/3m/6m when quote is newer than last history point', () => {
    for (const range of ['1m', '3m', '6m'] as const) {
      const pts = [makePoint('2026-08-18T00:00:00.000Z', 100)];
      const result = appendCurrentQuotePoint(pts, range, 105, '2026-08-19T14:30:00.000Z', false);
      expect(result).toHaveLength(2);
      expect(result[1]).toMatchObject({
        closeChart: 105,
        volumeChart: null,
        timestamp: '2026-08-19T14:30:00.000Z',
      });
    }
  });

  // Ranges that do not use daily candles must not get a synthetic point.
  it('does not append for non-daily-candle ranges (1y, 1w, 24h, today)', () => {
    const pts = [makePoint('2026-08-18T00:00:00.000Z', 100)];
    for (const range of ['1y', '1w', '24h', 'today', '5y', '3y'] as const) {
      const result = appendCurrentQuotePoint(pts, range, 105, '2026-08-19T14:30:00.000Z', false);
      expect(result).toHaveLength(1);
    }
  });

  // Stale quotes must never be appended.
  it('does not append a stale quote', () => {
    const pts = [makePoint('2026-08-18T00:00:00.000Z', 100)];
    const result = appendCurrentQuotePoint(pts, '1m', 105, '2026-08-19T14:30:00.000Z', true);
    expect(result).toHaveLength(1);
  });

  // If quote timestamp is on the same UTC date as the last history point, no duplicate is added.
  it('does not append when quote date equals last history point date (prevents duplicates)', () => {
    const pts = [makePoint('2026-08-19T00:00:00.000Z', 100)];
    const result = appendCurrentQuotePoint(pts, '1m', 105, '2026-08-19T14:30:00.000Z', false);
    expect(result).toHaveLength(1);
  });

  // If quote timestamp is before or equal to the last history point, no append.
  it('does not append when quote timestamp is at or before last history point', () => {
    const pts = [makePoint('2026-08-18T00:00:00.000Z', 100)];
    // Same millisecond
    const r1 = appendCurrentQuotePoint(pts, '1m', 105, '2026-08-18T00:00:00.000Z', false);
    expect(r1).toHaveLength(1);
    // Earlier
    const r2 = appendCurrentQuotePoint(pts, '1m', 105, '2026-08-17T12:00:00.000Z', false);
    expect(r2).toHaveLength(1);
  });

  // Weekend: quote is on a Saturday — we still append (no trading-day validation here; the
  // existing safe-quote semantics only check staleness).
  it('appends a quote that falls on a weekend when it is newer than the last history point', () => {
    // 2026-08-15 is Saturday
    const pts = [makePoint('2026-08-14T00:00:00.000Z', 100)];
    const result = appendCurrentQuotePoint(pts, '1m', 101, '2026-08-15T10:00:00.000Z', false);
    expect(result).toHaveLength(2);
  });

  // If there are no history points, no synthetic point is appended (nothing to extend).
  it('does not append when there are no existing history points', () => {
    const result = appendCurrentQuotePoint([], '1m', 105, '2026-08-19T14:30:00.000Z', false);
    expect(result).toHaveLength(0);
  });

  // Null price means no append.
  it('does not append when quotePrice is null', () => {
    const pts = [makePoint('2026-08-18T00:00:00.000Z', 100)];
    const result = appendCurrentQuotePoint(pts, '1m', null, '2026-08-19T14:30:00.000Z', false);
    expect(result).toHaveLength(1);
  });

  // Null timestamp means no append.
  it('does not append when quoteTimestampUtc is null', () => {
    const pts = [makePoint('2026-08-18T00:00:00.000Z', 100)];
    const result = appendCurrentQuotePoint(pts, '1m', 105, null, false);
    expect(result).toHaveLength(1);
  });

  // The original points array must remain unmodified (immutability).
  it('does not mutate the original points array', () => {
    const pts = [makePoint('2026-08-18T00:00:00.000Z', 100)];
    const original = [...pts];
    appendCurrentQuotePoint(pts, '1m', 105, '2026-08-19T14:30:00.000Z', false);
    expect(pts).toEqual(original);
  });

  // Longer ranges (5y, 3y, 1y) do not regress.
  it('longer ranges return the same array reference unchanged', () => {
    const pts = [makePoint('2026-08-18T00:00:00.000Z', 100)];
    for (const range of ['5y', '3y', '1y'] as const) {
      const result = appendCurrentQuotePoint(pts, range, 105, '2026-08-19T14:30:00.000Z', false);
      expect(result).toBe(pts);
    }
  });
});
