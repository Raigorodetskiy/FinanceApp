import React from 'react';
import { describe, expect, it } from 'vitest';
import type { MarketIndex } from '../types';
import {
  buildSidebarMenuItems,
  MARKET_INDICES_MANAGE_KEY,
  MARKET_INDICES_SIDEBAR_PARENT_KEY,
  marketIndexRoute,
  marketIndexSidebarKey,
  STOCKS_DIRECTORIES_PARENT_KEY,
} from './AppSidebar';

const makeIndex = (overrides: Partial<MarketIndex> & { id: number; code: string; name: string }): MarketIndex => ({
  description: '',
  countryOrRegion: '',
  sortOrder: 0,
  isArchived: false,
  showInNavigation: true,
  providerSymbol: null,
  ...overrides,
});

describe('AppSidebar market indices hierarchy', () => {
  it('keeps top-level ordering with directories immediately after stocks', () => {
    const items = buildSidebarMenuItems({
      portfolios: [],
      marketIndices: [],
      onNavigate: () => undefined,
    });

    expect(items?.map((item) => item?.key)).toEqual([
      'dashboard',
      'portfolios',
      'stocks',
      STOCKS_DIRECTORIES_PARENT_KEY,
    ]);
  });

  it('places market indices as the second child under stocks and not at top level', () => {
    const items = buildSidebarMenuItems({
      portfolios: [],
      marketIndices: [],
      onNavigate: () => undefined,
    });

    const topLevelKeys = items?.map((item) => item?.key) ?? [];
    expect(topLevelKeys).not.toContain(MARKET_INDICES_SIDEBAR_PARENT_KEY);

    const stocksItem = items?.find((item) => item?.key === 'stocks');
    const stockChildren = stocksItem?.children ?? [];
    expect(stockChildren.map((item) => item?.key)).toEqual([
      'stocks-list',
      MARKET_INDICES_SIDEBAR_PARENT_KEY,
    ]);
  });

  it('keeps management first and appends only visible dynamic indices', () => {
    const items = buildSidebarMenuItems({
      portfolios: [],
      marketIndices: [
        makeIndex({ id: 1, code: 'SPX', name: 'S&P 500' }),
        makeIndex({ id: 2, code: 'ARCH', name: 'Archived', isArchived: true }),
        makeIndex({ id: 3, code: 'HIDE', name: 'Hidden', showInNavigation: false }),
        makeIndex({ id: 4, code: 'DAX', name: 'DAX' }),
      ],
      onNavigate: () => undefined,
    });

    const stocksItem = items?.find((item) => item?.key === 'stocks');
    const marketIndicesItem = stocksItem?.children?.find((item) => item?.key === MARKET_INDICES_SIDEBAR_PARENT_KEY);
    const children = marketIndicesItem?.children ?? [];

    expect(children.map((item) => item?.key)).toEqual([
      MARKET_INDICES_MANAGE_KEY,
      marketIndexSidebarKey(1),
      marketIndexSidebarKey(4),
    ]);
  });

  it('preserves market index leaf routing keys', () => {
    expect(marketIndexSidebarKey(7)).toBe('market-index-7');
    expect(marketIndexRoute(7)).toBe('/market-indices/7');
  });

  it('navigates through the manage item and visible dynamic index items', () => {
    const routes: string[] = [];
    const items = buildSidebarMenuItems({
      portfolios: [],
      marketIndices: [makeIndex({ id: 5, code: 'NDX', name: 'Nasdaq 100' })],
      onNavigate: (route) => routes.push(route),
    });

    const stocksItem = items?.find((item) => item?.key === 'stocks');
    const marketIndicesItem = stocksItem?.children?.find((item) => item?.key === MARKET_INDICES_SIDEBAR_PARENT_KEY);
    const children = marketIndicesItem?.children ?? [];

    expect(React.isValidElement(marketIndicesItem?.icon as React.ReactNode)).toBe(true);
    (children[0]?.onClick as (() => void) | undefined)?.();
    (children[1]?.onClick as (() => void) | undefined)?.();

    expect(routes).toEqual(['/market-indices', '/market-indices/5']);
  });
});
