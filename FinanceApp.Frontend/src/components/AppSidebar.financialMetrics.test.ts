import { describe, expect, it } from 'vitest';
import {
  applySidebarOpenChange,
  computeSidebarOpenKeys,
} from './AppSidebar';

describe('AppSidebar directories routes', () => {
  it('opens only top-level directories for /financial-metrics', () => {
    const keys = computeSidebarOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      marketIndicesOpen: false,
      selectedKeys: ['financial-metrics'],
    });

    expect(keys).toContain('stocks-directories');
    expect(keys).not.toContain('stocks');
    expect(keys).not.toContain('market-indices-root');
  });

  it('allows stocks to stay closed on a directory route', () => {
    const state = applySidebarOpenChange({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: true,
      marketIndicesOpen: false,
      selectedKeys: ['financial-metrics'],
      newOpenKeys: ['stocks-directories'],
    });

    expect(state.stocksOpen).toBe(false);
    expect(state.stocksDirectoriesOpen).toBe(true);
  });

  it('keeps directories open when the route requires them', () => {
    const state = applySidebarOpenChange({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: true,
      marketIndicesOpen: false,
      selectedKeys: ['financial-metrics'],
      newOpenKeys: [],
    });

    const keys = computeSidebarOpenKeys({
      ...state,
      selectedKeys: ['financial-metrics'],
    });

    expect(keys).toContain('stocks-directories');
    expect(keys).not.toContain('stocks');
  });

  it('opens only top-level directories for /help', () => {
    const keys = computeSidebarOpenKeys({
      portfoliosOpen: false,
      stocksOpen: false,
      stocksDirectoriesOpen: false,
      marketIndicesOpen: false,
      selectedKeys: ['help'],
    });

    expect(keys).toContain('stocks-directories');
    expect(keys).not.toContain('stocks');
    expect(keys).not.toContain('market-indices-root');
  });
});
