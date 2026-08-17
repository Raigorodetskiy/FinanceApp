import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';
import {
  getConstituentTableRowKey,
  getTrackButtonState,
  INDEX_CONSTITUENTS_TOTAL_COLS,
  makeConstituentRows,
} from './IndexConstituentsPanel';
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

describe('IndexConstituentsPanel row expansion contracts', () => {
  it('builds an extra chart row only for the expanded stock', () => {
    const rows = makeConstituentRows([
      makeConstituent({ stockId: 1, ticker: 'AAPL' }),
      makeConstituent({ stockId: 2, ticker: 'MSFT' }),
    ], 2);

    expect(rows).toHaveLength(3);
    expect(getConstituentTableRowKey(rows[0]!)).toBe('1');
    expect(getConstituentTableRowKey(rows[1]!)).toBe('2');
    expect(getConstituentTableRowKey(rows[2]!)).toBe('chart-2');
  });

  it('keeps full-width chart row span aligned with stock-table column count', () => {
    expect(INDEX_CONSTITUENTS_TOTAL_COLS).toBe(8);
  });
});

describe('IndexConstituentsPanel plus-action state contracts', () => {
  it('keeps icon action enabled for CatalogOnly', () => {
    const state = getTrackButtonState('CatalogOnly', false);
    expect(state.isTracked).toBe(false);
    expect(state.disabled).toBe(false);
    expect(state.loading).toBe(false);
    expect(state.ariaLabel).toBe('Добавить в список акций');
    expect(state.tooltip).toBe('Добавить в список акций');
  });

  it('keeps same action visible but disabled for Tracked with tooltip', () => {
    const state = getTrackButtonState('Tracked', false);
    expect(state.isTracked).toBe(true);
    expect(state.disabled).toBe(true);
    expect(state.ariaLabel).toBe('Добавлена в список акций');
    expect(state.tooltip).toBe('Уже добавлена в список акций');
  });

  it('locks action in loading state to prevent duplicate submissions', () => {
    const state = getTrackButtonState('CatalogOnly', true);
    expect(state.loading).toBe(true);
    expect(state.disabled).toBe(true);
  });
});

describe('IndexConstituentsPanel action/behavior contracts', () => {
  it('renders fundamentals action and does not render edit/delete actions', () => {
    expect(panelSource).toContain('FundOutlined');
    expect(panelSource).not.toContain('EditOutlined');
    expect(panelSource).not.toContain('DeleteOutlined');
  });

  it('keeps source metadata, archived refresh guard, and local tracking update behavior', () => {
    expect(panelSource).toContain('sourceMeta.source || sourceMeta.asOfDate');
    expect(panelSource).toContain('!isArchived && (');
    expect(panelSource).toContain("trackingStatus: 'Tracked'");
  });

  it('does not perform per-row stock detail requests (no getStock/getStockHistory/getStockFundamentals imports)', () => {
    expect(panelSource).not.toContain('getStock(');
    expect(panelSource).not.toContain('getStockHistory');
    expect(panelSource).not.toContain('getStockFundamentals');
  });
});
