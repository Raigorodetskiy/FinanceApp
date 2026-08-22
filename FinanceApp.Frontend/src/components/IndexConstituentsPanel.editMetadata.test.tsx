// @vitest-environment jsdom
import React from 'react';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import IndexConstituentsPanel from './IndexConstituentsPanel';

const { buildUpdateStockMetadataPayloadMock } = vi.hoisted(() => ({
  buildUpdateStockMetadataPayloadMock: vi.fn(() => ({
    name: 'Align Technology Updated',
    commonName: 'Align',
    currentPrice: 111,
    sectorId: 10,
    industryId: null,
    marketIndexIds: [1],
  })),
}));

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
  default: ({ open, onSubmit }: { open: boolean; onSubmit: (values: unknown) => void }) => (
    open
      ? (
        <button
          type="button"
          onClick={() => onSubmit({
            ticker: 'DAXA',
            name: 'Align Technology Updated',
            exchange: 'Frankfurt',
            currentPrice: 111,
            sectorId: 10,
            industryId: undefined,
            marketIndexIds: [1],
          })}
        >
          submit-edit
        </button>
      )
      : null
  ),
  buildUpdateStockMetadataPayload: buildUpdateStockMetadataPayloadMock,
  loadStockMetadataLookups: vi.fn().mockResolvedValue({
    sectors: [],
    marketIndices: [],
    marketIndicesLoadFailed: false,
  }),
}));

vi.mock('./StockFundamentalsDrawer', () => ({ default: () => null }));
vi.mock('./StockPriceChart', () => ({ default: () => null }));

describe('IndexConstituentsPanel edit metadata submission', () => {
  beforeEach(async () => {
    vi.clearAllMocks();
    const api = await import('../services/api');
    vi.mocked(api.getIndexConstituents).mockResolvedValue({
      data: {
        marketIndexId: 1,
        indexName: 'DAX',
        totalCount: 1,
        constituents: [
          {
            stockId: 1,
            ticker: 'DAXA',
            name: 'Align Technology',
            commonName: 'Align',
            exchange: 'Frankfurt',
            sector: 'Financials',
            industry: null,
            trackingStatus: 'CatalogOnly',
            importedAt: '2026-08-19T00:00:00Z',
          },
        ],
      },
    });
    vi.mocked(api.getStock).mockResolvedValue({
      data: {
        id: 1,
        ticker: 'DAXA',
        name: 'Align Technology',
        commonName: 'Align',
        exchange: 'Frankfurt',
        currentPrice: 100,
        updatedAt: '2026-08-19T00:00:00Z',
        sector: { id: 9, name: 'Financials', isArchived: false },
        marketIndexIds: [1],
      },
    });
  });

  afterEach(() => {
    cleanup();
  });

  it('passes sectorId through build payload into updateStockMetadata', async () => {
    const api = await import('../services/api');
    const user = userEvent.setup();

    render(<IndexConstituentsPanel indexId={1} isArchived={false} />);
    await waitFor(() => expect(screen.getByText('DAXA')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Редактировать акцию' }));
    await user.click(await screen.findByRole('button', { name: 'submit-edit' }));

    await waitFor(() => expect(buildUpdateStockMetadataPayloadMock).toHaveBeenCalled());
    await waitFor(() => expect(vi.mocked(api.updateStockMetadata)).toHaveBeenCalledWith(
      1,
      expect.objectContaining({
        sectorId: 10,
        industryId: null,
      }),
    ));
  });
});
