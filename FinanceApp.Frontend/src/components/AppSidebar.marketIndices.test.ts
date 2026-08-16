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
      key === 'stocks'
      || key === 'stocks-list'
      || key === 'sectors'
      || key === 'market-indices'
      || key === 'financial-metrics'
      || key.startsWith('stocks-'),
  );
  const hasStocksDirectoriesSelection = selectedKeys.some(
    (key) =>
      key === 'stocks-directories'
      || key === 'sectors'
      || key === 'market-indices'
      || key === 'financial-metrics'
      || key.startsWith('sectors-')
      || key.startsWith('market-indices-'),
  );

  if (portfoliosOpen || activePortfolioId != null) keys.push('portfolios');
  if (stocksOpen || hasStocksSelection) keys.push('stocks');
  if ((stocksOpen || hasStocksSelection) && (stocksDirectoriesOpen || hasStocksDirectoriesSelection)) {
    keys.push('stocks-directories');
  }
  if (activePortfolioId != null) keys.push(`${PORTFOLIO_KEY_PREFIX}${activePortfolioId}`);
  if (defaultOpenKeys) {
    for (const key of defaultOpenKeys) {
      if (!keys.includes(key)) keys.push(key);
    }
  }

  return keys;
}

describe('AppSidebar open state – market-indices route', () => {
  it('route /market-indices forces stocks and stocks-directories open', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['market-indices'],
    });

    expect(keys).toContain('stocks');
    expect(keys).toContain('stocks-directories');
  });

  it('market-indices is the selected leaf key', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['market-indices'],
    });

    expect(keys).not.toContain('market-indices');
    expect(keys).toContain('stocks');
    expect(keys).toContain('stocks-directories');
  });

  it('market-indices route does not force portfolios open', () => {
    const keys = computeOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      selectedKeys: ['market-indices'],
    });

    expect(keys).not.toContain('portfolios');
  });
});
