// @vitest-environment jsdom
import React from 'react';
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import IndexConstituentsPanel from './IndexConstituentsPanel';

vi.mock('../services/api', () => ({
  getIndexConstituentHistory: vi.fn(),
  getIndexConstituents: vi.fn(),
  getIndexConstituentHistoryRefreshJob: vi.fn(),
  getIndexConstituentPerformance: vi.fn(),
  getStock: vi.fn(),
  getStockPrice: vi.fn(),
  refreshIndexConstituentHistory: vi.fn(),
  refreshIndexConstituents: vi.fn(),
  refreshIndexConstituentsHistory: vi.fn(),
  startIndexConstituentsBatchQuoteRefresh: vi.fn(),
  getIndexConstituentsBatchQuoteRefreshJob: vi.fn(),
  trackStock: vi.fn(),
  updateStockMetadata: vi.fn(),
  updateStockQuote: vi.fn(),
}));

vi.mock('./StockEditModal', () => ({
  default: () => null,
  buildUpdateStockMetadataPayload: vi.fn(() => ({})),
  loadStockMetadataLookups: vi.fn().mockResolvedValue({
    sectors: [],
    marketIndices: [],
    marketIndicesLoadFailed: false,
  }),
}));

vi.mock('./StockFundamentalsDrawer', () => ({ default: () => null }));
vi.mock('./StockPriceChart', () => ({ default: () => null }));

const baseConstituentsResponse = {
  data: {
    marketIndexId: 1,
    indexName: 'DAX',
    totalCount: 3,
    constituents: [
      { stockId: 1, ticker: 'DAXA', name: 'Dax Member', commonName: 'Dax Member', sector: 'Information Technology', industry: 'Software', exchange: 'Frankfurt', trackingStatus: 'CatalogOnly', importedAt: '2026-08-19T00:00:00Z' },
      { stockId: 2, ticker: 'OTHR', name: 'Other Index', commonName: 'Other Index', sector: 'Financials', industry: null, exchange: 'NYSE', trackingStatus: 'CatalogOnly', importedAt: '2026-08-19T00:00:00Z' },
      { stockId: 3, ticker: 'NONE', name: 'No Index', commonName: 'No Index', exchange: 'NYSE', trackingStatus: 'CatalogOnly', importedAt: '2026-08-19T00:00:00Z' },
    ],
  },
};

const buildPerformanceResponse = (range: string, items: Array<{ stockId: number; changePercent: number | null }>) => ({
  data: {
    marketIndexId: 1,
    range,
    generatedAtUtc: '2026-08-19T00:00:00Z',
    items: items.map((item) => ({
      stockId: item.stockId,
      startPrice: item.changePercent == null ? null : 100,
      endPrice: item.changePercent == null ? null : 100 * (1 + item.changePercent / 100),
      changePercent: item.changePercent,
      startAtUtc: '2026-08-18T00:00:00Z',
      endAtUtc: '2026-08-19T00:00:00Z',
      dataStatus: item.changePercent == null ? 'InsufficientData' : 'Available',
    })),
  },
});

describe('IndexConstituentsPanel 24h performance integration', () => {
  beforeEach(async () => {
    vi.clearAllMocks();
    const api = await import('../services/api');
    vi.mocked(api.getIndexConstituents).mockResolvedValue(baseConstituentsResponse);
    vi.mocked(api.getIndexConstituentPerformance).mockResolvedValue(buildPerformanceResponse('24h', [
      { stockId: 3, changePercent: 7 },
      { stockId: 1, changePercent: 2 },
      { stockId: 2, changePercent: null },
    ]));
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
  });

  it('requests 24h performance and sorts globally with unavailable values last', async () => {
    const user = userEvent.setup();
    const api = await import('../services/api');

    render(<IndexConstituentsPanel indexId={1} isArchived={false} />);
    await waitFor(() => expect(screen.getAllByText('DAXA').length).toBeGreaterThan(0));

    const sortSelect = screen.getAllByRole('combobox', { name: 'Сортировка' })[0]!;
    await user.click(sortSelect);
    await user.click(await screen.findByText('24 ч.'));

    await waitFor(() => expect(vi.mocked(api.getIndexConstituentPerformance)).toHaveBeenCalledWith(1, '24h', expect.any(AbortSignal)));
    await waitFor(() => expect(screen.getByText('+7,00 %')).toBeInTheDocument());

    const rows = Array.from(document.querySelectorAll('tr[data-row-key]'));
    const ids = rows.map((row) => row.getAttribute('data-row-key'));
    expect(ids.indexOf('3')).toBeLessThan(ids.indexOf('1'));
    expect(ids.indexOf('1')).toBeLessThan(ids.indexOf('2'));
  });

  it('renders visible classification text in constituent table without legacy compact tags', async () => {
    render(<IndexConstituentsPanel indexId={1} isArchived={false} />);
    await waitFor(() => expect(screen.getAllByText('DAXA').length).toBeGreaterThan(0));

    const daxRow = document.querySelector('tr[data-row-key="1"]');
    const otherRow = document.querySelector('tr[data-row-key="2"]');
    const noneRow = document.querySelector('tr[data-row-key="3"]');
    expect(daxRow).not.toBeNull();
    expect(otherRow).not.toBeNull();
    expect(noneRow).not.toBeNull();

    expect(screen.getByText('Information Technology · Software')).toBeInTheDocument();
    expect(screen.getByText('Financials')).toBeInTheDocument();
    expect(screen.queryByText('СЕК')).not.toBeInTheDocument();
    expect(screen.queryByText('ОТР')).not.toBeInTheDocument();

    const daxName = within(daxRow as HTMLElement).getByText('Dax Member');
    const daxClassification = within(daxRow as HTMLElement).getByLabelText('Классификация: Information Technology · Software');
    expect(daxClassification).toHaveAttribute('title', 'Information Technology · Software');
    expect(daxClassification.parentElement).toContainElement(daxName);
    expect(daxClassification.parentElement).toHaveStyle({ display: 'flex', width: '100%' });
    expect(daxName).toHaveStyle({ fontSize: '16px' });
    expect(daxClassification).toHaveStyle({ marginLeft: 'auto', textAlign: 'right', fontSize: '14px' });
    expect(daxClassification.closest('.ant-tag')).toBeNull();
    expect((noneRow as HTMLElement).querySelector('[aria-label^="Классификация:"]')).toBeNull();
  });

  it('does not let stale older range responses overwrite the latest selection', async () => {
    const user = userEvent.setup();
    const api = await import('../services/api');

    let resolve1y: ((value: unknown) => void) | null = null;
    vi.mocked(api.getIndexConstituentPerformance).mockImplementation((_, range) => {
      if (range === '1y') {
        return new Promise((resolve) => { resolve1y = resolve; });
      }
      return Promise.resolve(buildPerformanceResponse('24h', [
        { stockId: 3, changePercent: 7 },
        { stockId: 1, changePercent: 2 },
        { stockId: 2, changePercent: null },
      ]));
    });

    render(<IndexConstituentsPanel indexId={1} isArchived={false} />);
    await waitFor(() => expect(screen.getAllByText('DAXA').length).toBeGreaterThan(0));

    const sortSelect = screen.getAllByRole('combobox', { name: 'Сортировка' })[0]!;
    await user.click(sortSelect);
    await user.click(await screen.findByText('1 год'));
    await user.click(sortSelect);
    await user.click(await screen.findByText('24 ч.'));
    await waitFor(() => expect(screen.getByText('+7,00 %')).toBeInTheDocument());

    act(() => {
      resolve1y?.(buildPerformanceResponse('1y', [
        { stockId: 1, changePercent: 40 },
        { stockId: 2, changePercent: 30 },
        { stockId: 3, changePercent: 20 },
      ]));
    });

    await waitFor(() => expect(screen.getByText('+7,00 %')).toBeInTheDocument());
    expect(screen.queryByText('+40,00 %')).not.toBeInTheDocument();
  });

  it('shows a visible error state when the 24h performance request fails', async () => {
    const user = userEvent.setup();
    const api = await import('../services/api');
    vi.mocked(api.getIndexConstituentPerformance).mockRejectedValue(new Error('network'));

    render(<IndexConstituentsPanel indexId={1} isArchived={false} />);
    await waitFor(() => expect(screen.getAllByText('DAXA').length).toBeGreaterThan(0));

    const sortSelect = screen.getAllByRole('combobox', { name: 'Сортировка' })[0]!;
    await user.click(sortSelect);
    await user.click(await screen.findByText('24 ч.'));

    await waitFor(() => expect(screen.getByText('Ошибка загрузки данных о росте')).toBeInTheDocument());
  });
});
