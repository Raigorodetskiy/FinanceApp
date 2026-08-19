import { describe, expect, it } from 'vitest';
import {
  buildHistoryChartData,
  formatHistoryTimestamp,
  usesUtcDateLabels,
} from './stockPriceChartData';

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

  it('appends a newer valid current quote for 1m/3m/6m ranges', () => {
    const data = buildHistoryChartData([
      {
        timestamp: '2026-08-18T00:00:00.000Z',
        interval: '1d',
        openRaw: 20,
        highRaw: 20,
        lowRaw: 20,
        closeRaw: 20,
        openNormalized: 20,
        highNormalized: 20,
        lowNormalized: 20,
        closeNormalized: 20,
        openEur: 20,
        highEur: 20,
        lowEur: 20,
        closeEur: 20,
        volume: 300,
      },
    ], '1m', {
      timestampUtc: '2026-08-19T13:45:00.000Z',
      closeChart: 21,
      rawClose: 21,
    });

    expect(data.map((point) => point.timestamp)).toEqual([
      '2026-08-18T00:00:00.000Z',
      '2026-08-19T13:45:00.000Z',
    ]);
    expect(data[1]).toMatchObject({ closeChart: 21, volumeChart: null });
  });

  it('does not append a duplicate daily trading date when the current quote is on the same effective date', () => {
    const data = buildHistoryChartData([
      {
        timestamp: '2026-08-19T00:00:00.000Z',
        interval: '1d',
        openRaw: 20,
        highRaw: 20,
        lowRaw: 20,
        closeRaw: 20,
        openNormalized: 20,
        highNormalized: 20,
        lowNormalized: 20,
        closeNormalized: 20,
        openEur: 20,
        highEur: 20,
        lowEur: 20,
        closeEur: 20,
        volume: 300,
      },
    ], '3m', {
      timestampUtc: '2026-08-19T22:30:00.000Z',
      closeChart: 22,
      rawClose: 22,
    });

    expect(data).toHaveLength(1);
    expect(data[0]?.closeChart).toBe(20);
  });

  it('does not append stale current quotes', () => {
    const data = buildHistoryChartData([
      {
        timestamp: '2026-08-18T00:00:00.000Z',
        interval: '1d',
        openRaw: 20,
        highRaw: 20,
        lowRaw: 20,
        closeRaw: 20,
        openNormalized: 20,
        highNormalized: 20,
        lowNormalized: 20,
        closeNormalized: 20,
        openEur: 20,
        highEur: 20,
        lowEur: 20,
        closeEur: 20,
        volume: 300,
      },
    ], '6m', {
      timestampUtc: '2026-08-19T13:45:00.000Z',
      closeChart: 21,
      rawClose: 21,
      isStale: true,
    });

    expect(data).toHaveLength(1);
    expect(data[0]?.closeChart).toBe(20);
  });

  it('keeps date-only labels on UTC trading dates for daily ranges at weekend boundaries', () => {
    expect(usesUtcDateLabels('1m')).toBe(true);
    expect(formatHistoryTimestamp('2026-08-21T22:30:00.000Z', '1m', 'DD.MM.YYYY')).toBe('21.08.2026');
  });

  it('does not regress longer ranges by appending quote overlays outside 1m/3m/6m', () => {
    const data = buildHistoryChartData([
      {
        timestamp: '2026-08-18T00:00:00.000Z',
        interval: '1wk',
        openRaw: 20,
        highRaw: 20,
        lowRaw: 20,
        closeRaw: 20,
        openNormalized: 20,
        highNormalized: 20,
        lowNormalized: 20,
        closeNormalized: 20,
        openEur: 20,
        highEur: 20,
        lowEur: 20,
        closeEur: 20,
        volume: 300,
      },
    ], '1y', {
      timestampUtc: '2026-08-19T13:45:00.000Z',
      closeChart: 21,
      rawClose: 21,
    });

    expect(data).toHaveLength(1);
    expect(data[0]?.timestamp).toBe('2026-08-18T00:00:00.000Z');
  });
});
