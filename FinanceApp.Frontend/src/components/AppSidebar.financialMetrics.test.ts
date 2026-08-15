/**
 * Tests for AppSidebar open-state logic specific to the /financial-metrics route.
 * Mirrors the helper functions in AppSidebar.openState.test.ts with the updated
 * financial-metrics awareness added in PR #118.
 */

import { describe, expect, it } from 'vitest';

interface OpenState {
  portfoliosOpen: boolean;
  stocksOpen: boolean;
  stocksDirectoriesOpen: boolean;
}

interface OpenKeysParams extends OpenState {
  selectedKeys: string[];
  activePortfolioId?: string | number;
  defaultOpenKeys?: string[];
}

/** Mirrors the updated openKeys logic in AppSidebar.tsx */
function computeOpenKeys({
  portfoliosOpen,
  stocksOpen,
  stocksDirectoriesOpen,
  selectedKeys,
  activePortfolioId,
  defaultOpenKeys,
}: OpenKeysParams): string[] {
  const keys: string[] = [];
  const PORTFOLIO_KEY_PREFIX = 'portfolio-';

  const hasStocksSelection = selectedKeys.some(
    (key) =>
      key === 'stocks' ||
      key === 'stocks-list' ||
      key === 'sectors' ||
      key === 'financial-metrics' ||
      key.startsWith('stocks-'),
  );
  const hasStocksDirectoriesSelection = selectedKeys.some(
    (key) =>
      key === 'stocks-directories' ||
      key === 'sectors' ||
      key === 'financial-metrics' ||
      key.startsWith('sectors-'),
  );

  if (portfoliosOpen || activePortfolioId != null) keys.push('portfolios');
  if (stocksOpen || hasStocksSelection) keys.push('stocks');
  if ((stocksOpen || hasStocksSelection) && (stocksDirectoriesOpen || hasStocksDirectoriesSelection)) {
    keys.push('stocks-directories');
  }
  if (activePortfolioId != null) keys.push(`${PORTFOLIO_KEY_PREFIX}${activePortfolioId}`);
  if (defaultOpenKeys) {
    for (const k of defaultOpenKeys) {
      if (!keys.includes(k)) keys.push(k);
    }
  }
  return keys;
}

interface HandleOpenChangeParams extends OpenState {
  selectedKeys: string[];
  activePortfolioId?: string | number;
  newOpenKeys: string[];
}

/** Mirrors the updated handleMenuOpenChange logic in AppSidebar.tsx */
function applyMenuOpenChange({
  portfoliosOpen,
  stocksOpen,
  stocksDirectoriesOpen,
  selectedKeys,
  activePortfolioId,
  newOpenKeys,
}: HandleOpenChangeParams): OpenState {
  const currentKeys = computeOpenKeys({
    portfoliosOpen,
    stocksOpen,
    stocksDirectoriesOpen,
    selectedKeys,
    activePortfolioId,
  });

  let nextPortfoliosOpen = portfoliosOpen;
  let nextStocksOpen = stocksOpen;
  let nextStocksDirectoriesOpen = stocksDirectoriesOpen;

  const prevHasPortfolios = currentKeys.includes('portfolios');
  const nextHasPortfolios = newOpenKeys.includes('portfolios');
  if (prevHasPortfolios && !nextHasPortfolios && activePortfolioId == null) nextPortfoliosOpen = false;
  else if (!prevHasPortfolios && nextHasPortfolios) nextPortfoliosOpen = true;

  const prevHasStocks = currentKeys.includes('stocks');
  const nextHasStocks = newOpenKeys.includes('stocks');
  const routeRequiresStocks = selectedKeys.some(
    (key) =>
      key === 'stocks' ||
      key === 'stocks-list' ||
      key === 'sectors' ||
      key === 'financial-metrics' ||
      key.startsWith('stocks-'),
  );
  if (prevHasStocks && !nextHasStocks) {
    if (!routeRequiresStocks) {
      nextStocksOpen = false;
      nextStocksDirectoriesOpen = false;
    }
  } else if (!prevHasStocks && nextHasStocks) {
    nextStocksOpen = true;
  }

  const prevHasStocksDirectories = currentKeys.includes('stocks-directories');
  const nextHasStocksDirectories = newOpenKeys.includes('stocks-directories');
  const routeRequiresDirectories = selectedKeys.some(
    (key) => key === 'sectors' || key === 'financial-metrics' || key.startsWith('sectors-'),
  );
  if (prevHasStocksDirectories && !nextHasStocksDirectories) {
    if (!routeRequiresDirectories) nextStocksDirectoriesOpen = false;
  } else if (!prevHasStocksDirectories && nextHasStocksDirectories) {
    nextStocksDirectoriesOpen = true;
  }

  return { portfoliosOpen: nextPortfoliosOpen, stocksOpen: nextStocksOpen, stocksDirectoriesOpen: nextStocksDirectoriesOpen };
}

describe('AppSidebar open state – financial-metrics route', () => {
  it('route /financial-metrics forces stocks and stocks-directories open', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['financial-metrics'],
    });
    expect(keys).toContain('stocks');
    expect(keys).toContain('stocks-directories');
  });

  it('financial-metrics is the selected key (not sectors or stocks-list)', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['financial-metrics'],
    });
    // parent keys open, selected key itself not in openKeys
    expect(keys).not.toContain('financial-metrics');
    expect(keys).toContain('stocks');
    expect(keys).toContain('stocks-directories');
  });

  it('user cannot close stocks while on /financial-metrics route', () => {
    const state = applyMenuOpenChange({
      portfoliosOpen: false,
      stocksOpen: true,
      stocksDirectoriesOpen: true,
      selectedKeys: ['financial-metrics'],
      newOpenKeys: [],
    });
    const keys = computeOpenKeys({ ...state, selectedKeys: ['financial-metrics'] });
    expect(keys).toContain('stocks');
    expect(keys).toContain('stocks-directories');
  });

  it('user cannot close stocks-directories while on /financial-metrics route', () => {
    const state = applyMenuOpenChange({
      portfoliosOpen: false,
      stocksOpen: true,
      stocksDirectoriesOpen: true,
      selectedKeys: ['financial-metrics'],
      newOpenKeys: ['stocks'], // user tries to close directories
    });
    expect(state.stocksDirectoriesOpen).toBe(true);
  });

  it('navigating from /sectors to /financial-metrics keeps both submenus open', () => {
    const keysOnSectors = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: true,
      stocksDirectoriesOpen: true,
      selectedKeys: ['sectors'],
    });
    expect(keysOnSectors).toContain('stocks');
    expect(keysOnSectors).toContain('stocks-directories');

    const keysOnFinancialMetrics = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: true,
      stocksDirectoriesOpen: true,
      selectedKeys: ['financial-metrics'],
    });
    expect(keysOnFinancialMetrics).toContain('stocks');
    expect(keysOnFinancialMetrics).toContain('stocks-directories');
  });

  it('navigating to /financial-metrics does not affect portfolios open state', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: true,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['financial-metrics'],
    });
    expect(keys).toContain('portfolios');
    expect(keys).toContain('stocks');
    expect(keys).toContain('stocks-directories');
  });

  it('sectors route open state is unchanged by financial-metrics logic', () => {
    const keysForSectors = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['sectors'],
    });
    expect(keysForSectors).toContain('stocks');
    expect(keysForSectors).toContain('stocks-directories');
  });

  it('stocks-list route is not affected by financial-metrics changes', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['stocks-list'],
    });
    expect(keys).toContain('stocks');
    expect(keys).not.toContain('stocks-directories');
  });

  it('dashboard route still produces no stocks open keys', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['dashboard'],
    });
    expect(keys).not.toContain('stocks');
    expect(keys).not.toContain('stocks-directories');
    expect(keys).not.toContain('financial-metrics');
  });
});
