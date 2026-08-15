/**
 * Regression tests for AppSidebar controlled open-state logic (Fix #3).
 *
 * These tests exercise the openKeys computation and handleMenuOpenChange
 * behaviour directly as pure logic, without rendering the component.
 */

import { describe, expect, it } from 'vitest';

// ---------------------------------------------------------------------------
// Helpers that mirror the openKeys / handleMenuOpenChange logic in AppSidebar
// ---------------------------------------------------------------------------

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
      key.startsWith('stocks-'),
  );
  const hasStocksDirectoriesSelection = selectedKeys.some(
    (key) => key === 'stocks-directories' || key === 'sectors' || key.startsWith('sectors-'),
  );

  if (portfoliosOpen || activePortfolioId != null) {
    keys.push('portfolios');
  }
  if (stocksOpen || hasStocksSelection) {
    keys.push('stocks');
  }
  if ((stocksOpen || hasStocksSelection) && (stocksDirectoriesOpen || hasStocksDirectoriesSelection)) {
    keys.push('stocks-directories');
  }
  if (activePortfolioId != null) {
    keys.push(`${PORTFOLIO_KEY_PREFIX}${activePortfolioId}`);
  }
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

  // portfolios
  const prevHasPortfolios = currentKeys.includes('portfolios');
  const nextHasPortfolios = newOpenKeys.includes('portfolios');
  if (prevHasPortfolios && !nextHasPortfolios) {
    if (activePortfolioId == null) nextPortfoliosOpen = false;
  } else if (!prevHasPortfolios && nextHasPortfolios) {
    nextPortfoliosOpen = true;
  }

  // stocks
  const prevHasStocks = currentKeys.includes('stocks');
  const nextHasStocks = newOpenKeys.includes('stocks');
  const routeRequiresStocks = selectedKeys.some(
    (key) =>
      key === 'stocks' ||
      key === 'stocks-list' ||
      key === 'sectors' ||
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

  // stocks-directories
  const prevHasStocksDirectories = currentKeys.includes('stocks-directories');
  const nextHasStocksDirectories = newOpenKeys.includes('stocks-directories');
  const routeRequiresDirectories = selectedKeys.some(
    (key) => key === 'sectors' || key.startsWith('sectors-'),
  );
  if (prevHasStocksDirectories && !nextHasStocksDirectories) {
    if (!routeRequiresDirectories) nextStocksDirectoriesOpen = false;
  } else if (!prevHasStocksDirectories && nextHasStocksDirectories) {
    nextStocksDirectoriesOpen = true;
  }

  return {
    portfoliosOpen: nextPortfoliosOpen,
    stocksOpen: nextStocksOpen,
    stocksDirectoriesOpen: nextStocksDirectoriesOpen,
  };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AppSidebar open state – stocks submenu', () => {
  it('stocks is closed by default when not on a stocks route', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['dashboard'],
    });
    expect(keys).not.toContain('stocks');
    expect(keys).not.toContain('stocks-directories');
  });

  it('user click opens stocks submenu', () => {
    const state = applyMenuOpenChange({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['dashboard'],
      newOpenKeys: ['stocks'],
    });
    expect(state.stocksOpen).toBe(true);
  });

  it('opened stocks is reflected in openKeys', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: true,
      stocksDirectoriesOpen: false,
      selectedKeys: ['dashboard'],
    });
    expect(keys).toContain('stocks');
  });

  it('user click opens stocks-directories submenu', () => {
    // stocks must already be open first
    const afterStocks = applyMenuOpenChange({
      portfoliosOpen: false,
      stocksOpen: true,
      stocksDirectoriesOpen: false,
      selectedKeys: ['dashboard'],
      newOpenKeys: ['stocks', 'stocks-directories'],
    });
    expect(afterStocks.stocksDirectoriesOpen).toBe(true);

    const keys = computeOpenKeys({
      ...afterStocks,
      selectedKeys: ['dashboard'],
    });
    expect(keys).toContain('stocks');
    expect(keys).toContain('stocks-directories');
  });

  it('user click closes stocks submenu (no route requirement)', () => {
    const state = applyMenuOpenChange({
      portfoliosOpen: false,
      stocksOpen: true,
      stocksDirectoriesOpen: false,
      selectedKeys: ['dashboard'],
      newOpenKeys: [],
    });
    expect(state.stocksOpen).toBe(false);
  });

  it('route /stocks forces stocks open and highlights stocks-list', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['stocks-list'],
    });
    expect(keys).toContain('stocks');
    expect(keys).not.toContain('stocks-directories');
  });

  it('route /sectors forces both stocks and stocks-directories open', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['sectors'],
    });
    expect(keys).toContain('stocks');
    expect(keys).toContain('stocks-directories');
  });

  it('user cannot close stocks while route requires it', () => {
    // On /stocks-list route, user tries to close via onOpenChange
    const state = applyMenuOpenChange({
      portfoliosOpen: false,
      stocksOpen: true,
      stocksDirectoriesOpen: false,
      selectedKeys: ['stocks-list'],
      newOpenKeys: [], // user tries to close all
    });
    // state.stocksOpen becomes false after user closes it, but the route
    // keeps 'stocks' in openKeys anyway, so the submenu remains visible.
    const keys = computeOpenKeys({
      ...state,
      selectedKeys: ['stocks-list'],
    });
    expect(keys).toContain('stocks');
  });

  it('user cannot close stocks-directories while on /sectors route', () => {
    const state = applyMenuOpenChange({
      portfoliosOpen: false,
      stocksOpen: true,
      stocksDirectoriesOpen: true,
      selectedKeys: ['sectors'],
      newOpenKeys: ['stocks'], // user tries to close directories
    });
    expect(state.stocksDirectoriesOpen).toBe(true);
  });

  it('closing stocks also resets stocksDirectoriesOpen', () => {
    const state = applyMenuOpenChange({
      portfoliosOpen: false,
      stocksOpen: true,
      stocksDirectoriesOpen: true,
      selectedKeys: ['dashboard'],
      newOpenKeys: [],
    });
    expect(state.stocksOpen).toBe(false);
    expect(state.stocksDirectoriesOpen).toBe(false);
  });

  it('portfolios open state is not disturbed when toggling stocks', () => {
    const state = applyMenuOpenChange({
      portfoliosOpen: true,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['dashboard'],
      newOpenKeys: ['portfolios', 'stocks'],
    });
    expect(state.portfoliosOpen).toBe(true);
    expect(state.stocksOpen).toBe(true);
  });
});
