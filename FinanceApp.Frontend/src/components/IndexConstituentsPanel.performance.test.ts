import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';
import {
  formatPerformance,
  sortConstituentsByPerformance,
} from './performanceHelpers';
import type { PerformanceMap } from './performanceHelpers';
import {
  INDEX_CONSTITUENTS_TOTAL_COLS,
} from './IndexConstituentsPanel';
import { STOCK_HISTORY_RANGE_OPTIONS } from './historyRangeOptions';
import type { IndexConstituentDto } from '../types';

const __dirname = dirname(fileURLToPath(import.meta.url));
const panelSource = readFileSync(join(__dirname, 'IndexConstituentsPanel.tsx'), 'utf8');

const makeConstituent = (overrides: Partial<IndexConstituentDto> = {}): IndexConstituentDto => ({
  stockId: 1,
  ticker: 'AAPL',
  name: 'Apple Inc.',
  commonName: 'Apple',
  exchange: 'NASDAQ',
  trackingStatus: 'CatalogOnly',
  importedAt: '2026-08-17T00:00:00Z',
  ...overrides,
});

// ── formatPerformance ────────────────────────────────────────────────────────

describe('formatPerformance', () => {
  it('returns unavailable for null', () => {
    expect(formatPerformance(null).kind).toBe('unavailable');
  });

  it('returns unavailable for undefined', () => {
    expect(formatPerformance(undefined).kind).toBe('unavailable');
  });

  it('formats positive value with leading + and comma decimal separator', () => {
    const result = formatPerformance(12.45);
    expect(result.kind).toBe('available');
    if (result.kind !== 'available') return;
    expect(result.formatted).toContain('+');
    expect(result.formatted).toContain('12,45');
    expect(result.color).toBe('#389e0d'); // green
  });

  it('formats negative value with leading - and comma decimal separator', () => {
    const result = formatPerformance(-3.20);
    expect(result.kind).toBe('available');
    if (result.kind !== 'available') return;
    expect(result.formatted).toContain('-');
    expect(result.formatted).toContain('3,20');
    expect(result.color).toBe('#cf1322'); // red
  });

  it('formats zero with 0,00 and neutral color', () => {
    const result = formatPerformance(0);
    expect(result.kind).toBe('available');
    if (result.kind !== 'available') return;
    expect(result.formatted).toContain('0,00');
    expect(result.color).toBe('inherit');
  });

  it('does not produce -0,00 % for very small negative values', () => {
    const result = formatPerformance(-0.001);
    expect(result.kind).toBe('available');
    if (result.kind !== 'available') return;
    expect(result.formatted).not.toBe('-0,00\u00a0%');
    expect(result.formatted).toBe('0,00\u00a0%');
    expect(result.color).toBe('inherit');
  });

  it('does not contain a dot as decimal separator', () => {
    const pos = formatPerformance(1.5);
    const neg = formatPerformance(-1.5);
    if (pos.kind !== 'available' || neg.kind !== 'available') throw new Error();
    expect(pos.formatted).not.toContain('1.5');
    expect(neg.formatted).not.toContain('1.5');
  });
});

// ── sortConstituentsByPerformance ────────────────────────────────────────────

describe('sortConstituentsByPerformance', () => {
  const makeMap = (entries: [number, number | null][]): PerformanceMap =>
    new Map(entries);

  it('sorts gains first, then zero, then negative, then null', () => {
    const items = [
      makeConstituent({ stockId: 1 }),
      makeConstituent({ stockId: 2 }),
      makeConstituent({ stockId: 3 }),
      makeConstituent({ stockId: 4 }),
    ];
    const map = makeMap([[1, null], [2, -5], [3, 0], [4, 10]]);
    const sorted = sortConstituentsByPerformance(items, map);
    expect(sorted.map((c) => c.stockId)).toEqual([4, 3, 2, 1]);
  });

  it('sorts available values descending', () => {
    const items = [
      makeConstituent({ stockId: 1 }),
      makeConstituent({ stockId: 2 }),
      makeConstituent({ stockId: 3 }),
    ];
    const map = makeMap([[1, 5], [2, 20], [3, 10]]);
    const sorted = sortConstituentsByPerformance(items, map);
    expect(sorted.map((c) => c.stockId)).toEqual([2, 3, 1]);
  });

  it('places all null values last', () => {
    const items = [
      makeConstituent({ stockId: 1 }),
      makeConstituent({ stockId: 2 }),
      makeConstituent({ stockId: 3 }),
    ];
    const map = makeMap([[1, null], [2, null], [3, 5]]);
    const sorted = sortConstituentsByPerformance(items, map);
    expect(sorted[0]!.stockId).toBe(3);
    expect([sorted[1]!.stockId, sorted[2]!.stockId].sort()).toEqual([1, 2]);
  });

  it('breaks ties by stockId ascending', () => {
    const items = [
      makeConstituent({ stockId: 3 }),
      makeConstituent({ stockId: 1 }),
      makeConstituent({ stockId: 2 }),
    ];
    const map = makeMap([[1, 10], [2, 10], [3, 10]]);
    const sorted = sortConstituentsByPerformance(items, map);
    expect(sorted.map((c) => c.stockId)).toEqual([1, 2, 3]);
  });

  it('breaks null ties by stockId ascending', () => {
    const items = [
      makeConstituent({ stockId: 3 }),
      makeConstituent({ stockId: 1 }),
    ];
    const map: PerformanceMap = new Map();
    const sorted = sortConstituentsByPerformance(items, map);
    expect(sorted.map((c) => c.stockId)).toEqual([1, 3]);
  });

  it('does not mutate the original array', () => {
    const items = [
      makeConstituent({ stockId: 2 }),
      makeConstituent({ stockId: 1 }),
    ];
    const map = makeMap([[1, 20], [2, 5]]);
    sortConstituentsByPerformance(items, map);
    expect(items[0]!.stockId).toBe(2); // unchanged
  });
});

// ── Panel source contracts ────────────────────────────────────────────────────

describe('IndexConstituentsPanel performance column count', () => {
  it('updates total column count to accommodate performance column', () => {
    expect(INDEX_CONSTITUENTS_TOTAL_COLS).toBe(9);
  });
});

describe('IndexConstituentsPanel period selector contracts', () => {
  it('imports STOCK_HISTORY_RANGE_OPTIONS so chart and table periods stay in sync', () => {
    expect(panelSource).toContain('STOCK_HISTORY_RANGE_OPTIONS');
  });

  it('uses the exact same 9 range options as the stock chart', () => {
    expect(STOCK_HISTORY_RANGE_OPTIONS).toHaveLength(9);
    expect(STOCK_HISTORY_RANGE_OPTIONS.map((o) => o.value)).toEqual([
      'today', '24h', '1w', '1m', '3m', '6m', '1y', '3y', '5y',
    ]);
  });

  it('renders period selector with accessible label', () => {
    expect(panelSource).toContain('Рост за период');
    expect(panelSource).toContain('aria-label="Рост за период"');
  });

  it('defaults to 1y range', () => {
    expect(panelSource).toContain("toStockHistoryRange('1y')");
  });

  it('does not render a direction selector or laggards mode', () => {
    expect(panelSource).not.toContain('laggard');
    expect(panelSource).not.toContain('sortDirection');
    expect(panelSource).not.toContain('sortOrder');
    expect(panelSource).not.toContain("'ascend'");
    expect(panelSource).not.toContain("'descend'");
  });
});

describe('IndexConstituentsPanel performance API contracts', () => {
  it('imports getIndexConstituentPerformance from api', () => {
    expect(panelSource).toContain('getIndexConstituentPerformance');
  });

  it('uses AbortController to cancel stale requests', () => {
    expect(panelSource).toContain('AbortController');
    expect(panelSource).toContain('performanceAbortRef');
  });

  it('uses performanceRangeRef to avoid stale range closures', () => {
    expect(panelSource).toContain('performanceRangeRef');
    expect(panelSource).toContain('performanceRangeRef.current');
  });

  it('clears performanceMap when range changes', () => {
    expect(panelSource).toContain('setPerformanceMap(new Map())');
  });

  it('shows loading indicator for performance column while fetching', () => {
    expect(panelSource).toContain('performanceLoading');
  });

  it('shows dash with tooltip for unavailable performance', () => {
    expect(panelSource).toContain('Недостаточно исторических данных');
  });

  it('reloads performance after constituent data reload', () => {
    expect(panelSource).toContain('void loadPerformance(performanceRangeRef.current)');
  });

  it('cancels performance request on unmount/indexId change', () => {
    expect(panelSource).toContain('performanceAbortRef.current?.abort()');
  });
});

describe('IndexConstituentsPanel sorting contracts', () => {
  it('sorts constituents by performance before filtering', () => {
    expect(panelSource).toContain('sortConstituentsByPerformance');
    expect(panelSource).toContain('performanceMap');
  });

  it('imports formatPerformance from performanceHelpers', () => {
    expect(panelSource).toContain('formatPerformance');
  });
});
