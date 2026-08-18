// @vitest-environment jsdom
import React from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import AuthContext from '../contexts/AuthContext';
import type { Stock } from '../types';
import StocksPage from './StocksPage';

const trackedStock: Stock = {
  id: 1,
  ticker: 'AAPL',
  name: 'Apple Inc.',
  commonName: 'Apple',
  exchange: 'NYSE',
  currentPrice: 100,
  updatedAt: '2026-08-18T00:00:00Z',
  trackingStatus: 1,
  marketIndexIds: [1],
};

const untrackedStock: Stock = {
  id: 2,
  ticker: 'BAS',
  name: 'BASF SE',
  commonName: 'BASF',
  exchange: 'Frankfurt',
  currentPrice: 50,
  updatedAt: '2026-08-18T00:00:00Z',
  trackingStatus: 0,
  marketIndexIds: [],
};

vi.mock('../services/api', async () => {
  const actual = await vi.importActual<typeof import('../services/api')>('../services/api');
  return {
    ...actual,
    getTrackedStocks: vi.fn(),
    getStockCatalog: vi.fn(),
    getPortfolios: vi.fn().mockResolvedValue({ data: [] }),
    getSectors: vi.fn().mockResolvedValue([]),
    getMarketIndices: vi.fn().mockResolvedValue([
      {
        id: 1,
        name: 'S&P 500',
        code: 'SPX',
        providerSymbol: null,
        description: '',
        countryOrRegion: '',
        sortOrder: 1,
        isArchived: false,
        showInNavigation: true,
      },
    ]),
    getStockPrice: vi.fn(),
    trackStock: vi.fn().mockResolvedValue({ data: {} }),
    untrackStock: vi.fn().mockResolvedValue({ data: {} }),
  };
});

const authValue = {
  token: 'token',
  user: { id: 1, username: 'test', email: 'test@example.com', roles: [] },
  login: () => {},
  logout: () => {},
  refreshUser: async () => {},
  isAuthenticated: true,
  loading: false,
};

const renderPage = (mode: 'tracked' | 'catalog') => render(
  <MemoryRouter initialEntries={[mode === 'catalog' ? '/stocks/catalog' : '/stocks']}>
    <AuthContext.Provider value={authValue}>
      <StocksPage mode={mode} />
    </AuthContext.Provider>
  </MemoryRouter>,
);

describe('StocksPage catalog/tracking behavior', () => {
  beforeEach(async () => {
    vi.clearAllMocks();
    const api = await import('../services/api');
    vi.mocked(api.getTrackedStocks).mockResolvedValue({ data: [trackedStock] });
    vi.mocked(api.getStockCatalog).mockResolvedValue({ data: [trackedStock, untrackedStock] });
  });

  it('renders catalog entries with ticker, exchange and tracking statuses', async () => {
    renderPage('catalog');

    await waitFor(() => {
      expect(screen.getByText('AAPL')).toBeInTheDocument();
      expect(screen.getByText('BAS')).toBeInTheDocument();
    });

    expect(screen.getByText('Отслеживается')).toBeInTheDocument();
    expect(screen.getByText('Не отслеживается')).toBeInTheDocument();
    expect(screen.getByText('S&P 500')).toBeInTheDocument();
  });

  it('tracks from catalog and untracks from tracked page via non-destructive APIs', async () => {
    const user = userEvent.setup();
    const api = await import('../services/api');

    const catalog = renderPage('catalog');
    await waitFor(() => expect(screen.getAllByText('Добавить в отслеживаемые').length).toBeGreaterThan(0));
    await act(async () => {
      await user.click(screen.getAllByText('Добавить в отслеживаемые')[0]);
    });
    expect(api.trackStock).toHaveBeenCalledWith(2);

    catalog.unmount();
    const tracked = renderPage('tracked');
    await waitFor(() => expect(screen.getByLabelText('Удалить из отслеживаемых')).toBeInTheDocument());
    await act(async () => {
      await user.click(screen.getByLabelText('Удалить из отслеживаемых'));
    });
    await act(async () => {
      await user.click(screen.getByText('Да'));
    });
    expect(api.untrackStock).toHaveBeenCalledWith(1);
    tracked.unmount();
  });
});
