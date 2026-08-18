import { describe, expect, it } from 'vitest';
import {
  applySidebarOpenChange,
  computeSidebarOpenKeys,
  marketIndexSidebarKey,
} from './AppSidebar';

describe('AppSidebar open state', () => {
  it('keeps /stocks opening only the stocks submenu', () => {
    const keys = computeSidebarOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      marketIndicesOpen: false,
      selectedKeys: ['stocks-list'],
    });

    expect(keys).toContain('stocks');
    expect(keys).not.toContain('stocks-directories');
    expect(keys).not.toContain('market-indices-root');
  });

  it('opens stocks and nested market indices on an index route', () => {
    const keys = computeSidebarOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      marketIndicesOpen: false,
      selectedKeys: [marketIndexSidebarKey(5)],
    });

    expect(keys).toEqual(['stocks', 'market-indices-root']);
  });

  it('opens top-level directories independently for /sectors', () => {
    const keys = computeSidebarOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      marketIndicesOpen: false,
      selectedKeys: ['sectors'],
    });

    expect(keys).toContain('stocks-directories');
    expect(keys).not.toContain('stocks');
    expect(keys).not.toContain('market-indices-root');
  });

  it('route-required parents win over stale persisted preferences', () => {
    const keys = computeSidebarOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      marketIndicesOpen: true,
      selectedKeys: [marketIndexSidebarKey(7)],
    });

    expect(keys).toEqual(['stocks', 'market-indices-root']);
  });

  it('opening nested market indices also persists stocks as open', () => {
    const state = applySidebarOpenChange({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      marketIndicesOpen: false,
      selectedKeys: ['dashboard'],
      newOpenKeys: ['stocks', 'market-indices-root'],
    });

    expect(state.stocksOpen).toBe(true);
    expect(state.marketIndicesOpen).toBe(true);
  });

  it('closing stocks clears nested market indices when the route does not require them', () => {
    const state = applySidebarOpenChange({
      portfoliosOpen: false,
      stocksOpen: true,
      stocksDirectoriesOpen: false,
      marketIndicesOpen: true,
      selectedKeys: ['dashboard'],
      newOpenKeys: [],
    });

    expect(state.stocksOpen).toBe(false);
    expect(state.marketIndicesOpen).toBe(false);
  });

  it('closing directories on a non-directory route only updates the directories state', () => {
    const state = applySidebarOpenChange({
      portfoliosOpen: false,
      stocksOpen: true,
      stocksDirectoriesOpen: true,
      marketIndicesOpen: false,
      selectedKeys: ['dashboard'],
      newOpenKeys: ['stocks'],
    });

    expect(state.stocksOpen).toBe(true);
    expect(state.stocksDirectoriesOpen).toBe(false);
    expect(state.marketIndicesOpen).toBe(false);
  });

  it('cannot close required nested market indices on an index route', () => {
    const state = applySidebarOpenChange({
      portfoliosOpen: false,
      stocksOpen: true,
      stocksDirectoriesOpen: false,
      marketIndicesOpen: true,
      selectedKeys: [marketIndexSidebarKey(3)],
      newOpenKeys: ['stocks'],
    });

    const keys = computeSidebarOpenKeys({
      ...state,
      selectedKeys: [marketIndexSidebarKey(3)],
    });

    expect(keys).toContain('stocks');
    expect(keys).toContain('market-indices-root');
  });
});
