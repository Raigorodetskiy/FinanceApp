import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';
import {
  getConstituentTableRowKey,
  getTrackButtonState,
  INDEX_CONSTITUENTS_TOTAL_COLS,
  mergeEditedStockIntoConstituents,
  makeConstituentRows,
} from './IndexConstituentsPanel';
import type { IndexConstituentDto, Stock } from '../types';

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
    expect(INDEX_CONSTITUENTS_TOTAL_COLS).toBe(9);
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
  it('renders fundamentals and edit actions but still does not render delete actions', () => {
    expect(panelSource).toContain('FundOutlined');
    expect(panelSource).toContain('EditOutlined');
    expect(panelSource).not.toContain('DeleteOutlined');
  });

  it('keeps source metadata, archived refresh guard, and local tracking update behavior', () => {
    expect(panelSource).toContain('sourceMeta.source || sourceMeta.asOfDate');
    expect(panelSource).toContain("trackingStatus: 'Tracked'");
  });

  it('loads authoritative stock details before editing but still avoids direct row history/fundamentals imports', () => {
    expect(panelSource).toContain('getStock(');
    expect(panelSource).toContain('updateStockMetadata(');
    expect(panelSource).toContain('StockEditModal');
    expect(panelSource).not.toContain('getStockHistory');
    expect(panelSource).not.toContain('getStockFundamentals');
  });

  it('labels the new row edit action accessibly in Russian', () => {
    expect(panelSource).toContain('aria-label="Редактировать акцию"');
    expect(panelSource).toContain('<Tooltip title="Редактировать акцию">');
  });

  it('keeps quote-refresh control and removes row-level history-refresh control', () => {
    expect(panelSource).toContain('aria-label={`Обновить цену ${record.ticker}`}');
    expect(panelSource).not.toContain('aria-label={`Обновить исторические данные ${record.ticker}`}');
  });

  it('passes index-scoped history loader and async job adapter to expanded chart', () => {
    expect(panelSource).toContain('indexId={indexId}');
    expect(panelSource).toContain('liveQuote={live?.quote ?? null}');
    expect(panelSource).toContain('storedPriceEur={stock?.currentPrice ?? null}');
    expect(panelSource).toContain('storedPriceChangeEur={stock?.currentPriceChange ?? null}');
    expect(panelSource).toContain('historyLoader={loadConstituentHistory}');
    expect(panelSource).toContain('historyRefreshJobAdapter={constituentHistoryRefreshJobAdapter}');
    expect(panelSource).toContain('refreshIndexConstituentHistory');
    expect(panelSource).toContain('getIndexConstituentHistoryRefreshJob');
  });

  it('uses shared newest-snapshot resolver for price/change/time fields', () => {
    expect(panelSource).toContain('resolveNewestCurrentPriceSnapshot');
    expect(panelSource).toContain('const selectedSnapshotByStockId = useMemo(() => {');
    expect(panelSource).toContain('const getSelectedSnapshot = useCallback((record: IndexConstituentDto) => {');
    expect(panelSource).toContain('const selectedSnapshot = getSelectedSnapshot(record);');
    expect(panelSource).toContain('const ts = selectedSnapshot.currentPriceAt ?? null;');
  });

  it('keeps batch history button with confirmation and archived/empty loading guards', () => {
    expect(panelSource).toContain('Обновить историю акций');
    expect(panelSource).toContain('window.confirm(\'Обновить историю всех акций текущего состава?');
    expect(panelSource).toContain('isArchived');
    expect(panelSource).toContain('constituents.length === 0');
    expect(panelSource).toContain('batchHistoryRefreshing');
  });

  it('passes chart refresh token for expanded chart updates after refresh actions', () => {
    expect(panelSource).toContain('refreshToken={chartRefreshTokens[record._stockId] ?? 0}');
    expect(panelSource).toContain('setChartRefreshTokens');
  });

  it('keeps async history job polling lifecycle with cleanup and existing-job attachment', () => {
    expect(panelSource).toContain('onIndexHistoryRefreshStateChange');
    expect(panelSource).toContain('handleChartHistoryRefreshStateChange');
    expect(panelSource).toContain("Object.values(historyRefreshStates).some((state) => state === 'Queued' || state === 'Running')");
    expect(panelSource).toContain('setHistoryRefreshStates({})');
  });

  it('shows distinct persistence-failure text without pretending the provider fetch failed', () => {
    expect(panelSource).toContain('QUOTE_PERSIST_FAILURE_MESSAGE');
    expect(panelSource).toContain('Цена получена, но не удалось сохранить её');
  });

  it('shows compact sector/industry badges in the name cell', () => {
    expect(panelSource).toContain('StockClassificationBadges');
    expect(panelSource).toContain('sector={record.sector ?? null}');
    expect(panelSource).toContain('industry={record.industry ?? null}');
  });
});

describe('IndexConstituentsPanel edit reconciliation helpers', () => {
  const constituent = makeConstituent({
    stockId: 1,
    providerSymbol: 'AAPL',
    currentPrice: 100,
    currentPriceChange: 2,
    currentPriceChangePercent: 1.5,
    currentPriceAt: '2026-08-18T00:00:00Z',
  });

  it('updates the existing row immediately when the stock remains in the current index', () => {
    const updatedStock: Stock = {
      id: 1,
      ticker: 'AAPL',
      providerSymbol: 'AAPL',
      name: 'Apple Inc. Updated',
      commonName: 'Apple',
      exchange: 'NASDAQ',
      currentPrice: 110,
      currentPriceChange: null,
      currentPriceChangePercent: null,
      currentPriceAt: null,
      updatedAt: '2026-08-18T01:00:00Z',
      marketIndexIds: [5, 7],
      trackingStatus: 0,
    };

    const result = mergeEditedStockIntoConstituents([constituent], updatedStock, 5);

    expect(result).toHaveLength(1);
    expect(result[0]).toMatchObject({
      stockId: 1,
      name: 'Apple Inc. Updated',
      currentPrice: 110,
      currentPriceChange: null,
      currentPriceChangePercent: null,
      currentPriceAt: null,
      trackingStatus: 'CatalogOnly',
    });
  });

  it('removes the row immediately when the edited stock is no longer assigned to the current index', () => {
    const updatedStock: Stock = {
      id: 1,
      ticker: 'AAPL',
      name: 'Apple Inc.',
      commonName: 'Apple',
      exchange: 'NASDAQ',
      currentPrice: 100,
      updatedAt: '2026-08-18T01:00:00Z',
      marketIndexIds: [7],
      trackingStatus: 1,
    };

    expect(mergeEditedStockIntoConstituents([constituent], updatedStock, 5)).toEqual([]);
  });
});
