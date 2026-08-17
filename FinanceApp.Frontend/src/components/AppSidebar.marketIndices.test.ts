import { describe, expect, it } from 'vitest';
import {
  MARKET_INDICES_SIDEBAR_PARENT_KEY,
  MARKET_INDEX_KEY_PREFIX,
  marketIndexSidebarKey,
  marketIndexRoute,
} from './AppSidebar';

const PORTFOLIO_KEY_PREFIX = 'portfolio-';

interface OpenState {
  portfoliosOpen: boolean;
  stocksOpen: boolean;
  stocksDirectoriesOpen: boolean;
  marketIndicesOpen: boolean;
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
  marketIndicesOpen,
  selectedKeys,
  activePortfolioId,
  defaultOpenKeys,
}: OpenKeysParams): string[] {
  const keys: string[] = [];

  const hasStocksSelection = selectedKeys.some(
    (key) =>
      key === 'stocks'
      || key === 'stocks-list'
      || key === 'sectors'
      || key === 'financial-metrics'
      || key.startsWith('stocks-'),
  );
  const hasStocksDirectoriesSelection = selectedKeys.some(
    (key) =>
      key === 'stocks-directories'
      || key === 'sectors'
      || key === 'financial-metrics'
      || key.startsWith('sectors-'),
  );
  const hasMarketIndicesSelection = selectedKeys.some(
    (key) => key === MARKET_INDICES_SIDEBAR_PARENT_KEY || key.startsWith(MARKET_INDEX_KEY_PREFIX),
  );

  if (portfoliosOpen || activePortfolioId != null) keys.push('portfolios');
  if (stocksOpen || hasStocksSelection) keys.push('stocks');
  if ((stocksOpen || hasStocksSelection) && (stocksDirectoriesOpen || hasStocksDirectoriesSelection)) {
    keys.push('stocks-directories');
  }
  if (marketIndicesOpen || hasMarketIndicesSelection) keys.push(MARKET_INDICES_SIDEBAR_PARENT_KEY);
  if (activePortfolioId != null) keys.push(`${PORTFOLIO_KEY_PREFIX}${activePortfolioId}`);
  if (defaultOpenKeys) {
    for (const key of defaultOpenKeys) {
      if (!keys.includes(key)) keys.push(key);
    }
  }

  return keys;
}

describe('AppSidebar – market-indices top-level placement', () => {
  it('MARKET_INDICES_SIDEBAR_PARENT_KEY is the top-level submenu key', () => {
    expect(MARKET_INDICES_SIDEBAR_PARENT_KEY).toBe('market-indices-root');
  });

  it('marketIndexSidebarKey builds stable per-index keys', () => {
    expect(marketIndexSidebarKey(7)).toBe(`${MARKET_INDEX_KEY_PREFIX}7`);
    expect(marketIndexSidebarKey(42)).toBe(`${MARKET_INDEX_KEY_PREFIX}42`);
  });

  it('marketIndexRoute builds correct per-index URL', () => {
    expect(marketIndexRoute(7)).toBe('/market-indices/7');
    expect(marketIndexRoute(42)).toBe('/market-indices/42');
  });

  it('individual index route opens market-indices-root, not stocks', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      marketIndicesOpen: false,
      selectedKeys: [marketIndexSidebarKey(5)],
    });

    expect(keys).toContain(MARKET_INDICES_SIDEBAR_PARENT_KEY);
    expect(keys).not.toContain('stocks');
    expect(keys).not.toContain('stocks-directories');
    expect(keys).not.toContain('portfolios');
  });

  it('individual index route does not open portfolios', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      marketIndicesOpen: false,
      selectedKeys: [marketIndexSidebarKey(3)],
    });

    expect(keys).not.toContain('portfolios');
  });

  it('market-indices-root parent key is not included in selectedKeys for a child route', () => {
    // The selected leaf is market-index-{id}; parent key goes to openKeys only
    const selectedKey = marketIndexSidebarKey(10);
    expect(selectedKey).not.toBe(MARKET_INDICES_SIDEBAR_PARENT_KEY);
  });

  it('market-indices-manage selected key does not open stocks', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      marketIndicesOpen: false,
      selectedKeys: ['market-indices-manage'],
    });

    expect(keys).not.toContain('stocks');
    expect(keys).not.toContain('stocks-directories');
  });

  it('sectors route does not open market-indices-root', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      marketIndicesOpen: false,
      selectedKeys: ['sectors'],
    });

    expect(keys).not.toContain(MARKET_INDICES_SIDEBAR_PARENT_KEY);
    expect(keys).toContain('stocks');
    expect(keys).toContain('stocks-directories');
  });
});
