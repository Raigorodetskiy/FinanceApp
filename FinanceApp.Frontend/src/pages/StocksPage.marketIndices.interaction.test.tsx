// @vitest-environment jsdom
/**
 * StocksPage integration tests: real data flow from loadStockMetadataLookups → AppSidebar.
 *
 * Confirmed Bug #2: StocksPage loaded marketIndices into local state but did not pass
 * marketIndices={marketIndices} to AuthenticatedShell, so the sidebar always received
 * an empty array on /stocks.
 *
 * These tests render the full StocksPage → AuthenticatedShell → AppSidebar chain with
 * real Ant Design Menu. Only API/network services are mocked.
 */
import React from 'react';
import { render, screen, act, cleanup, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import AuthContext from '../contexts/AuthContext';
import StocksPage from './StocksPage';
import type { MarketIndex } from '../types';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SP500: MarketIndex = {
  id: 1,
  code: 'SPX',
  name: 'S&P 500',
  description: '',
  countryOrRegion: '',
  sortOrder: 1,
  isArchived: false,
  showInNavigation: true,
};

const DAX: MarketIndex = {
  id: 2,
  code: 'DAX',
  name: 'DAX 40',
  description: '',
  countryOrRegion: '',
  sortOrder: 2,
  isArchived: false,
  showInNavigation: true,
};

const ARCHIVED: MarketIndex = {
  id: 3,
  code: 'OLD',
  name: 'OldIndex',
  description: '',
  countryOrRegion: '',
  sortOrder: 3,
  isArchived: true,
  showInNavigation: true,
};

const HIDDEN_NAV: MarketIndex = {
  id: 4,
  code: 'HID',
  name: 'HiddenIndex',
  description: '',
  countryOrRegion: '',
  sortOrder: 4,
  isArchived: false,
  showInNavigation: false,
};

// ---------------------------------------------------------------------------
// Mock API / service layer
// ---------------------------------------------------------------------------

vi.mock('../services/api', async () => {
  const actual = await vi.importActual<typeof import('../services/api')>('../services/api');
  return {
    ...actual,
    getTrackedStocks: vi.fn().mockResolvedValue({ data: [] }),
    getStockCatalog: vi.fn().mockResolvedValue({ data: [] }),
    getPortfolios: vi.fn().mockResolvedValue({ data: [] }),
    getMe: vi.fn().mockResolvedValue({ data: { id: 1, username: 'testuser', email: 'test@example.com' } }),
    getSectors: vi.fn().mockResolvedValue([]),
    getMarketIndices: vi.fn(),
    getStockPrice: vi.fn().mockResolvedValue({ data: null }),
  };
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Minimal auth context that satisfies PrivateRoute (isAuthenticated, loading=false). */
const mockAuthValue = {
  token: 'fake-token',
  user: { id: 1, username: 'testuser', email: 'test@example.com', roles: [] },
  login: () => {},
  logout: () => {},
  refreshUser: async () => {},
  isAuthenticated: true,
  loading: false,
};

const LS_STOCKS = 'financeapp.sidebar.stocks.open';
const LS_MI = 'financeapp.sidebar.market-indices.open';

function renderStocksPage(localState: Record<string, string> = {}) {
  localStorage.clear();
  for (const [k, v] of Object.entries(localState)) {
    localStorage.setItem(k, v);
  }
  return render(
    <MemoryRouter initialEntries={['/stocks']}>
      <AuthContext.Provider value={mockAuthValue}>
        <StocksPage />
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

function getSubmenuTitle(text: string): HTMLElement {
  const el = screen.getByText(text);
  const title = el.closest('[aria-expanded]') ?? el.closest('[role="menuitem"]');
  if (!title) throw new Error(`Could not find submenu title for "${text}"`);
  return title as HTMLElement;
}

function isExpanded(text: string): boolean {
  return getSubmenuTitle(text).getAttribute('aria-expanded') === 'true';
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

beforeEach(() => {
  localStorage.clear();
  vi.clearAllMocks();
});

afterEach(() => {
  cleanup();
  localStorage.clear();
});

describe('StocksPage → AppSidebar market indices data flow (Bug #2)', () => {
  it('displays dynamic market index entries after loadStockMetadataLookups resolves', async () => {
    const { getMarketIndices } = await import('../services/api');
    vi.mocked(getMarketIndices).mockResolvedValue([SP500, DAX]);

    renderStocksPage({ [LS_STOCKS]: '1', [LS_MI]: '1' });

    // Open the Market Indices submenu
    await waitFor(() => expect(screen.getByText('Мировые индексы')).toBeInTheDocument());

    // After async resolution both dynamic entries must appear without further interaction
    await waitFor(() => {
      expect(screen.getByText('S&P 500')).toBeInTheDocument();
      expect(screen.getByText('DAX 40')).toBeInTheDocument();
    });
    expect(screen.getByText('Управление')).toBeInTheDocument();
  });

  it('filters out archived and showInNavigation=false entries', async () => {
    const { getMarketIndices } = await import('../services/api');
    vi.mocked(getMarketIndices).mockResolvedValue([SP500, ARCHIVED, HIDDEN_NAV]);

    renderStocksPage({ [LS_STOCKS]: '1', [LS_MI]: '1' });

    await waitFor(() => {
      expect(screen.getByText('S&P 500')).toBeInTheDocument();
    });

    expect(screen.queryByText('OldIndex')).not.toBeInTheDocument();
    expect(screen.queryByText('HiddenIndex')).not.toBeInTheDocument();
  });

  it('Market Indices remains open and loaded entries stay visible after clicking Отслеживаемые акции', async () => {
    const user = userEvent.setup();
    const { getMarketIndices } = await import('../services/api');
    vi.mocked(getMarketIndices).mockResolvedValue([SP500]);

    renderStocksPage({ [LS_STOCKS]: '1', [LS_MI]: '1' });

    await waitFor(() => {
      expect(screen.getByText('S&P 500')).toBeInTheDocument();
    });

    expect(isExpanded('Мировые индексы')).toBe(true);

    // Navigate to tracked stocks
    await act(async () => {
      await user.click(screen.getAllByText('Отслеживаемые акции')[0]);
    });

    // Market Indices must remain open and entries must still be visible
    expect(isExpanded('Мировые индексы')).toBe(true);
    expect(screen.getByText('S&P 500')).toBeInTheDocument();
    expect(screen.getByText('Управление')).toBeInTheDocument();
  });

  it('dynamic entries appear without closing/reopening the submenu when lookup resolves while open', async () => {
    const { getMarketIndices } = await import('../services/api');

    // Return a deferred promise so we control resolution timing
    let resolveMarketIndices!: (v: MarketIndex[]) => void;
    const deferred = new Promise<MarketIndex[]>((res) => { resolveMarketIndices = res; });
    vi.mocked(getMarketIndices).mockReturnValue(deferred);

    renderStocksPage({ [LS_STOCKS]: '1', [LS_MI]: '1' });

    await waitFor(() => expect(screen.getByText('Мировые индексы')).toBeInTheDocument());

    // Before resolution: Управление is present but no dynamic indices
    expect(screen.getByText('Управление')).toBeInTheDocument();
    expect(screen.queryByText('S&P 500')).not.toBeInTheDocument();

    // Resolve async lookup — entries must appear without submenu close/reopen
    await act(async () => {
      resolveMarketIndices([SP500, DAX]);
      await Promise.resolve();
    });

    await waitFor(() => {
      expect(screen.getByText('S&P 500')).toBeInTheDocument();
      expect(screen.getByText('DAX 40')).toBeInTheDocument();
    });

    // Submenu must still be open (no close/reopen cycle)
    expect(isExpanded('Мировые индексы')).toBe(true);
  });
});
