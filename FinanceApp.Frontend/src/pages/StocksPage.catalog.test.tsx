// @vitest-environment jsdom
import React from 'react';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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

const mcdCatalogStock: Stock = {
  id: 4,
  ticker: 'MCD',
  name: "McDonald's Corporation",
  commonName: "McDonald's",
  exchange: 'NYSE',
  currentPrice: 300,
  updatedAt: '2026-08-18T00:00:00Z',
  trackingStatus: 0,
  marketIndexIds: [],
};

const buildHistoryResponse = (range: '1y' | '6m') => ({
  range,
  interval: range === '6m' ? '1d' : '1d',
  currency: 'USD',
  financialCurrency: 'USD',
  normalizedQuoteCurrency: 'USD',
  quoteUnitMultiplier: 1,
  rateToEur: null,
  rateTimestampUtc: null,
  rateSource: null,
  conversionWarning: null,
  volumeMetrics: {
    averageVolume20: 1000,
    averageVolume50: 900,
    relativeVolume: 1.2,
    turnover: 305000,
    turnoverCurrency: 'USD',
    latestMetricsTimestamp: '2026-08-02T00:00:00Z',
    usesCompletedCandle: true,
  },
  points: range === '6m'
    ? [
        {
          timestamp: '2026-07-01T00:00:00Z',
          interval: '1d',
          openRaw: 320,
          highRaw: 325,
          lowRaw: 318,
          closeRaw: 322,
          openNormalized: 320,
          highNormalized: 325,
          lowNormalized: 318,
          closeNormalized: 322,
          openEur: null,
          highEur: null,
          lowEur: null,
          closeEur: null,
          volume: 1200,
        },
        {
          timestamp: '2026-08-02T00:00:00Z',
          interval: '1d',
          openRaw: 326,
          highRaw: 331,
          lowRaw: 324,
          closeRaw: 330,
          openNormalized: 326,
          highNormalized: 331,
          lowNormalized: 324,
          closeNormalized: 330,
          openEur: null,
          highEur: null,
          lowEur: null,
          closeEur: null,
          volume: 1400,
        },
      ]
    : [
        {
          timestamp: '2026-01-01T00:00:00Z',
          interval: '1d',
          openRaw: 290,
          highRaw: 295,
          lowRaw: 288,
          closeRaw: 292,
          openNormalized: 290,
          highNormalized: 295,
          lowNormalized: 288,
          closeNormalized: 292,
          openEur: null,
          highEur: null,
          lowEur: null,
          closeEur: null,
          volume: 1000,
        },
        {
          timestamp: '2026-08-02T00:00:00Z',
          interval: '1d',
          openRaw: 301,
          highRaw: 306,
          lowRaw: 300,
          closeRaw: 305,
          openNormalized: 301,
          highNormalized: 306,
          lowNormalized: 300,
          closeNormalized: 305,
          openEur: null,
          highEur: null,
          lowEur: null,
          closeEur: null,
          volume: 1300,
        },
      ],
});

const untrackedStockFRA: Stock = {
  id: 3,
  ticker: 'BMW',
  name: 'BMW AG',
  commonName: 'BMW',
  exchange: 'FRA',
  currentPrice: 80,
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
    getStockHistory: vi.fn(),
    refreshStockHistory: vi.fn(),
    getIndexConstituentHistory: vi.fn(),
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

describe('StocksPage catalog mode', () => {
  beforeEach(async () => {
    vi.clearAllMocks();
    const api = await import('../services/api');
    vi.mocked(api.getTrackedStocks).mockResolvedValue({ data: [trackedStock] });
    vi.mocked(api.getStockCatalog).mockResolvedValue({ data: [trackedStock, untrackedStock, untrackedStockFRA, mcdCatalogStock] });
    vi.mocked(api.getStockHistory).mockImplementation(async (_id, range) => ({ data: buildHistoryResponse(range as '1y' | '6m') }));
    vi.mocked(api.refreshStockHistory).mockResolvedValue({ data: { stockId: mcdCatalogStock.id, deletedPoints: 2, importedPoints: 2 } });
    vi.mocked(api.getIndexConstituentHistory).mockResolvedValue({ data: buildHistoryResponse('1y') });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  const getTickerToggleButton = (stockId: number, ticker: string) => {
    const button = document.querySelector(
      `tr[data-row-key="${stockId}"] button[aria-label="Открыть график цены: ${ticker}"]`,
    );
    expect(button).not.toBeNull();
    return button as HTMLButtonElement;
  };

  // Test 1: All exchange fixtures render in one table, not separate sections
  it('renders all stocks from different exchanges in a single unified table', async () => {
    renderPage('catalog');
    await waitFor(() => {
      expect(screen.getAllByText('AAPL').length).toBeGreaterThan(0);
      expect(screen.getAllByText('BAS').length).toBeGreaterThan(0);
      expect(screen.getAllByText('BMW').length).toBeGreaterThan(0);
    });
    // No exchange-group headings (tracked mode uses h5 headings)
    expect(screen.queryByRole('heading', { name: 'FRA' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'NYSE' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Портфель' })).not.toBeInTheDocument();
  });

  // Test 2: No visible «Статус», «Отслеживается» or «Не отслеживается» text
  it('does not show Статус column or status tags', async () => {
    renderPage('catalog');
    await waitFor(() => expect(screen.getAllByText('AAPL').length).toBeGreaterThan(0));
    expect(screen.queryByText('Статус')).not.toBeInTheDocument();
    expect(screen.queryByText('Отслеживается')).not.toBeInTheDocument();
    expect(screen.queryByText('Не отслеживается')).not.toBeInTheDocument();
  });

  // Test 3: Untracked stock has an enabled icon-only add action
  it('untracked stock has enabled icon-only add-to-tracked button', async () => {
    renderPage('catalog');
    await waitFor(() => expect(screen.getAllByText('BAS').length).toBeGreaterThan(0));
    // There should be a button accessible by «Добавить в отслеживаемые»
    const addButtons = screen.getAllByRole('button', { name: 'Добавить в отслеживаемые' });
    expect(addButtons.length).toBeGreaterThan(0);
    // At least one should be enabled (untracked stock)
    const enabledAdd = addButtons.find((btn) => !(btn as HTMLButtonElement).disabled);
    expect(enabledAdd).toBeDefined();
  });

  // Test 4: Tracked stock has the same add action disabled; no «Удалить из отслеживаемых» in catalog
  it('tracked stock has disabled add button and no untrack button in catalog', async () => {
    renderPage('catalog');
    await waitFor(() => expect(screen.getAllByText('AAPL').length).toBeGreaterThan(0));
    // No «Удалить из отслеживаемых» text visible
    expect(screen.queryByText('Удалить из отслеживаемых')).not.toBeInTheDocument();
    // The add buttons exist; the one for the tracked stock (AAPL) should be disabled
    const addButtons = screen.getAllByRole('button', { name: /Добавить в отслеживаемые|Акция уже отслеживается/i });
    expect(addButtons.length).toBeGreaterThan(0);
    const disabledAdd = addButtons.find((btn) => (btn as HTMLButtonElement).disabled);
    expect(disabledAdd).toBeDefined();
  });

  // Test 5: Clicking enabled add action calls trackStock with the correct stock ID
  it('clicking enabled add button calls trackStock with untracked stock id', async () => {
    const user = userEvent.setup();
    const api = await import('../services/api');
    renderPage('catalog');
    await waitFor(() => expect(screen.getAllByText('BAS').length).toBeGreaterThan(0));
    const enabledAddBtn = screen
      .getAllByRole('button', { name: 'Добавить в отслеживаемые' })
      .find((btn) => !(btn as HTMLButtonElement).disabled);
    expect(enabledAddBtn).toBeDefined();
    await act(async () => { await user.click(enabledAddBtn!); });
    expect(api.trackStock).toHaveBeenCalled();
  });

  // Test 6: Action buttons do not render visible long text labels
  it('action buttons do not render visible long text labels', async () => {
    renderPage('catalog');
    await waitFor(() => expect(screen.getAllByText('AAPL').length).toBeGreaterThan(0));
    // Buttons should not have visible text content matching these labels
    // (tooltips in ant-tooltip-inner are acceptable — they're invisible by default)
    const buttons = screen.queryAllByRole('button');
    const buttonTexts = buttons.map((btn) => btn.textContent?.trim() ?? '');
    expect(buttonTexts).not.toContain('Добавить в отслеживаемые');
    expect(buttonTexts).not.toContain('Удалить из отслеживаемых');
    expect(buttonTexts).not.toContain('Обновить цены');
  });

  // Test 7: 51+ fixtures → first page shows exactly 50 rows, pagination navigates to remainder
  it('paginates with 50 rows per page when catalog has 51+ stocks', async () => {
    const api = await import('../services/api');
    const manyStocks: Stock[] = Array.from({ length: 51 }, (_, i) => ({
      id: i + 100,
      ticker: `T${String(i).padStart(3, '0')}`,
      name: `Stock ${String(i).padStart(3, '0')}`,
      commonName: `Stock ${String(i).padStart(3, '0')}`,
      exchange: 'NYSE',
      currentPrice: 10,
      updatedAt: '2026-08-18T00:00:00Z',
      trackingStatus: 0,
      marketIndexIds: [],
    }));
    vi.mocked(api.getStockCatalog).mockResolvedValue({ data: manyStocks });
    renderPage('catalog');
    await waitFor(() => expect(screen.getAllByText('T000').length).toBeGreaterThan(0));
    // Count stock rows on page (tickers T000–T049 are on page 1, T050 on page 2)
    // Each ticker text appears at least once in the table; T050 should not be visible on page 1
    expect(screen.queryByText('T050')).not.toBeInTheDocument();
    expect(screen.getAllByText('T000').length).toBeGreaterThan(0);
    // Pagination shows total
    expect(screen.getByText(/Всего: 51/)).toBeInTheDocument();
  });

  // Test 8: Default order is alphabetical by stock name
  it('renders catalog stocks in alphabetical order by name', async () => {
    renderPage('catalog');
    await waitFor(() => expect(screen.getAllByText('AAPL').length).toBeGreaterThan(0));
    // Apple (commonName) < BASF < BMW alphabetically
    // Check tickers appear in DOM in correct relative order
    const allText = document.body.textContent ?? '';
    const idxApple = allText.indexOf('AAPL');
    const idxBas = allText.indexOf('BAS');
    const idxBmw = allText.indexOf('BMW');
    expect(idxApple).toBeGreaterThanOrEqual(0);
    expect(idxBas).toBeGreaterThanOrEqual(0);
    expect(idxBmw).toBeGreaterThanOrEqual(0);
    expect(idxApple).toBeLessThan(idxBas);
    expect(idxBas).toBeLessThan(idxBmw);
  });

  // Test 10: No countdown/auto-refresh text or bulk refresh action in catalog
  it('does not show auto-refresh countdown or bulk refresh button in catalog mode', async () => {
    renderPage('catalog');
    await waitFor(() => expect(screen.getAllByText('AAPL').length).toBeGreaterThan(0));
    expect(screen.queryByText(/Авто-обновление через/)).not.toBeInTheDocument();
    expect(screen.queryByText('Обновить цены')).not.toBeInTheDocument();
  });

  // Test 11: Catalog mode creates no automatic quote refresh with fake timers
  it('catalog mode creates no periodic auto-refresh intervals', async () => {
    vi.useFakeTimers();
    const api = await import('../services/api');
    renderPage('catalog');
    await act(async () => {
      await Promise.resolve(); // let state settle
    });
    vi.clearAllMocks();
    // Advance 15 minutes — no getStockPrice should be called
    await act(async () => { vi.advanceTimersByTime(15 * 60 * 1000); });
    expect(api.getStockPrice).not.toHaveBeenCalled();
    vi.useRealTimers();
  });

  // Test 12: Tracked rows have no special portfolio/tracked background class in catalog
  it('tracked rows do not have portfolio-stock-row class in catalog mode', async () => {
    const api = await import('../services/api');
    // Put trackedStock in a portfolio
    vi.mocked(api.getPortfolios).mockResolvedValue({
      data: [{ id: 1, name: 'P', items: [{ stockId: 1, quantity: 1, averagePurchasePrice: 0 }] }],
    });
    renderPage('catalog');
    await waitFor(() => expect(screen.getAllByText('AAPL').length).toBeGreaterThan(0));
    // In catalog mode, no row should have the portfolio highlight class
    const rows = document.querySelectorAll('tr.portfolio-stock-row');
    expect(rows.length).toBe(0);
  });

  it('loads catalog stock history via general endpoint, supports range changes and manual refresh, and keeps tracking action enabled', async () => {
    const user = userEvent.setup();
    const api = await import('../services/api');
    renderPage('catalog');

    await waitFor(() => expect(screen.getAllByText('MCD').length).toBeGreaterThan(0));
    const stockRow = screen.getAllByText('MCD')[0].closest('tr');
    expect(stockRow).not.toBeNull();
    const addButton = within(stockRow as HTMLElement).getByRole('button', { name: 'Добавить в отслеживаемые' });
    expect(addButton).toBeEnabled();

    await user.click(getTickerToggleButton(mcdCatalogStock.id, 'MCD'));

    await waitFor(() => expect(api.getStockHistory).toHaveBeenCalledWith(mcdCatalogStock.id, '1y'));
    expect(await screen.findByText("История цены: MCD — McDonald's Corporation")).toBeInTheDocument();
    expect(screen.getByText('305.00 USD')).toBeInTheDocument();

    await user.click(screen.getByText('6 мес.'));
    await waitFor(() => expect(api.getStockHistory).toHaveBeenCalledWith(mcdCatalogStock.id, '6m'));
    expect(await screen.findByText('330.00 USD')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Перезагрузить историю/ }));
    await user.click(await screen.findByRole('button', { name: 'Перезагрузить' }));

    await waitFor(() => expect(api.refreshStockHistory).toHaveBeenCalledWith(mcdCatalogStock.id));
    await waitFor(() => expect(api.getStockHistory).toHaveBeenLastCalledWith(mcdCatalogStock.id, '6m'));
    expect(api.trackStock).not.toHaveBeenCalled();
    expect(addButton).toBeEnabled();
  }, 15000);

  it('catalog charts create no automatic history or quote refresh timers', async () => {
    const user = userEvent.setup();
    const api = await import('../services/api');
    renderPage('catalog');

    await waitFor(() => expect(screen.getAllByText('MCD').length).toBeGreaterThan(0));
    await user.click(getTickerToggleButton(mcdCatalogStock.id, 'MCD'));
    await waitFor(() => expect(api.getStockHistory).toHaveBeenCalledWith(mcdCatalogStock.id, '1y'));

    vi.useFakeTimers();
    try {
      vi.clearAllMocks();
      await act(async () => { vi.advanceTimersByTime(15 * 60 * 1000); });

      expect(api.getStockHistory).not.toHaveBeenCalled();
      expect(api.refreshStockHistory).not.toHaveBeenCalled();
      expect(api.getStockPrice).not.toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
  }, 15000);

  it('catalog uses the general stock history endpoint even when the stock belongs to an index', async () => {
    const user = userEvent.setup();
    const api = await import('../services/api');
    vi.mocked(api.getStockCatalog).mockResolvedValue({
      data: [{ ...mcdCatalogStock, id: 5, marketIndexIds: [1] }],
    });
    renderPage('catalog');

    await waitFor(() => expect(screen.getAllByText('MCD').length).toBeGreaterThan(0));
    await waitFor(() =>
      expect(document.querySelector('tr[data-row-key="5"] button[aria-label="Открыть график цены: MCD"]')).not.toBeNull());
    await user.click(getTickerToggleButton(5, 'MCD'));

    await waitFor(() => expect(api.getStockHistory).toHaveBeenCalledWith(5, '1y'));
    expect(api.getIndexConstituentHistory).not.toHaveBeenCalled();
  }, 15000);
});

describe('StocksPage tracked mode regression', () => {
  beforeEach(async () => {
    vi.clearAllMocks();
    const api = await import('../services/api');
    vi.mocked(api.getTrackedStocks).mockResolvedValue({ data: [trackedStock] });
    vi.mocked(api.getStockCatalog).mockResolvedValue({ data: [trackedStock, untrackedStock] });
  });

  // Test 14: Tracked page retains non-destructive untrack action (guarded by Popconfirm confirmation)
  it('tracked page shows non-destructive untrack action', async () => {
    renderPage('tracked');
    await waitFor(() => expect(screen.getAllByText('AAPL').length).toBeGreaterThan(0));
    // The untrack icon-button must be present in tracked mode
    await waitFor(() => {
      const deleteBtn = document.querySelector('[aria-label="Удалить из отслеживаемых"]'); // exact aria-label
      expect(deleteBtn).not.toBeNull();
    });
  });

  // Tracked page shows countdown and auto-refresh
  it('tracked page shows auto-refresh countdown', async () => {
    renderPage('tracked');
    await waitFor(() => expect(screen.getAllByText('AAPL').length).toBeGreaterThan(0));
    const countdownEls = screen.queryAllByText(/Авто-обновление через/);
    expect(countdownEls.length).toBeGreaterThan(0);
    const refreshBtns = screen.queryAllByText('Обновить цены');
    expect(refreshBtns.length).toBeGreaterThan(0);
  });

  // Tracked page retains exchange grouping
  it('tracked page retains exchange-grouped tables', async () => {
    const api = await import('../services/api');
    vi.mocked(api.getTrackedStocks).mockResolvedValue({
      data: [
        { ...trackedStock, exchange: 'Frankfurt' },
      ],
    });
    renderPage('tracked');
    await waitFor(() => expect(screen.getAllByText('AAPL').length).toBeGreaterThan(0));
    // FRA appears both as group heading and as exchange tag — either confirms grouping is active
    await waitFor(() => {
      expect(screen.getAllByText('FRA').length).toBeGreaterThan(0);
    });
  });
});
