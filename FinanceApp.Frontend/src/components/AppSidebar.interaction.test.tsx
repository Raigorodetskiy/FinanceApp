// @vitest-environment jsdom
/**
 * Real DOM interaction tests for AppSidebar with actual Ant Design Menu.
 *
 * Why PR #154 and #155 failed:
 * Both PRs used a dual-control model: `onTitleClick` directly mutated
 * `marketIndicesOpen` state, while `onOpenChange` also processed the same event.
 * Ant Design fires `onTitleClick` before `onOpenChange`, so the direct toggle
 * ran first and then `onOpenChange` re-processed the new key array and undid the
 * change (because the explicit-toggle flag wasn't set when `onOpenChange` ran).
 * Additionally, Ant Design's `onOpenChange` *is* called after leaf item navigation
 * during certain re-render cycles, causing the code to incorrectly close the
 * submenu when clicking «Отслеживаемые акции».
 *
 * Fix: Remove `onTitleClick` from the market-indices submenu item entirely.
 * Use `onOpenChange` as the *single authority* for submenu state.
 * Since `onOpenChange` is only fired on explicit submenu open/close (not on leaf
 * clicks), any diff between current and new keys is genuine user intent.
 */
import React from 'react';
import { render, screen, act, cleanup } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import AppSidebar from './AppSidebar';
import type { MarketIndex } from '../types';

const LS_STOCKS = 'financeapp.sidebar.stocks.open';
const LS_MI = 'financeapp.sidebar.market-indices.open';
const LS_STOCKS_DIR = 'financeapp.sidebar.stocks-directories.open';

const SP500: MarketIndex = {
  id: 1,
  code: 'SPX',
  name: 'S&P 500',
  description: '',
  countryOrRegion: '',
  sortOrder: 0,
  isArchived: false,
  showInNavigation: true,
};

/** Captures the current router location so tests can assert navigation. */
let capturedPathname = '/';
function LocationCapture() {
  const loc = useLocation();
  React.useEffect(() => {
    capturedPathname = loc.pathname;
  });
  return null;
}

function renderSidebar(opts: {
  selectedKeys?: string[];
  localState?: Record<string, string>;
} = {}) {
  capturedPathname = '/';
  localStorage.clear();
  if (opts.localState) {
    for (const [k, v] of Object.entries(opts.localState)) {
      localStorage.setItem(k, v);
    }
  }
  return render(
    <MemoryRouter>
      <LocationCapture />
      <AppSidebar
        portfolios={[]}
        selectedKeys={opts.selectedKeys ?? []}
        onLogout={() => {}}
        marketIndices={[SP500]}
      />
    </MemoryRouter>,
  );
}

/** Find the Ant Design submenu title element that has aria-expanded. */
function getSubmenuTitle(text: string): HTMLElement {
  const el = screen.getByText(text);
  const title = el.closest('[aria-expanded]') ?? el.closest('[role="menuitem"]');
  if (!title) throw new Error(`Could not find submenu title for "${text}"`);
  return title as HTMLElement;
}

function isExpanded(text: string): boolean {
  return getSubmenuTitle(text).getAttribute('aria-expanded') === 'true';
}

beforeEach(() => {
  localStorage.clear();
  capturedPathname = '/';
});

afterEach(() => {
  cleanup();
  localStorage.clear();
});

describe('AppSidebar real DOM interaction — Market Indices toggle', () => {
  it('1. Market Indices initially closed → real click on title opens it (aria-expanded=true, Управление visible)', async () => {
    const user = userEvent.setup();
    // Stocks open, market indices closed
    renderSidebar({ localState: { [LS_STOCKS]: '1', [LS_MI]: '0' } });

    expect(isExpanded('Мировые индексы')).toBe(false);

    await act(async () => {
      await user.click(getSubmenuTitle('Мировые индексы'));
    });

    expect(isExpanded('Мировые индексы')).toBe(true);
    expect(screen.getByText('Управление')).toBeInTheDocument();
  });

  it('2. Second real click on title closes it (aria-expanded=false)', async () => {
    const user = userEvent.setup();
    renderSidebar({ localState: { [LS_STOCKS]: '1', [LS_MI]: '0' } });

    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });
    expect(isExpanded('Мировые индексы')).toBe(true);

    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });
    expect(isExpanded('Мировые индексы')).toBe(false);
  });

  it('3. Third real click re-opens it', async () => {
    const user = userEvent.setup();
    renderSidebar({ localState: { [LS_STOCKS]: '1', [LS_MI]: '0' } });

    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });
    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });
    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });

    expect(isExpanded('Мировые индексы')).toBe(true);
  });

  it('4. Market Indices open → click «Отслеживаемые акции» navigates to /stocks and Market Indices stays expanded', async () => {
    const user = userEvent.setup();
    renderSidebar({ localState: { [LS_STOCKS]: '1', [LS_MI]: '1' } });

    expect(isExpanded('Мировые индексы')).toBe(true);
    expect(screen.getByText('Управление')).toBeInTheDocument();

    await act(async () => {
      await user.click(screen.getByText('Отслеживаемые акции'));
    });

    expect(capturedPathname).toBe('/stocks');
    // Market Indices must remain open — key regression assertion
    expect(isExpanded('Мировые индексы')).toBe(true);
    expect(screen.getByText('Управление')).toBeInTheDocument();
  });

  it('5. Market Indices closed → click «Отслеживаемые акции» leaves it closed', async () => {
    const user = userEvent.setup();
    renderSidebar({ localState: { [LS_STOCKS]: '1', [LS_MI]: '0' } });

    expect(isExpanded('Мировые индексы')).toBe(false);

    await act(async () => {
      await user.click(screen.getByText('Отслеживаемые акции'));
    });

    expect(capturedPathname).toBe('/stocks');
    expect(isExpanded('Мировые индексы')).toBe(false);
  });

  it('5b. Market Indices open → click «Список акций» navigates to /stocks/catalog and Market Indices stays expanded', async () => {
    const user = userEvent.setup();
    renderSidebar({ localState: { [LS_STOCKS]: '1', [LS_MI]: '1' } });

    expect(isExpanded('Мировые индексы')).toBe(true);

    await act(async () => {
      await user.click(screen.getByText('Список акций'));
    });

    expect(capturedPathname).toBe('/stocks/catalog');
    expect(isExpanded('Мировые индексы')).toBe(true);
  });

  it('6. Closing Market Indices keeps Акции expanded', async () => {
    const user = userEvent.setup();
    renderSidebar({ localState: { [LS_STOCKS]: '1', [LS_MI]: '1' } });

    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });

    expect(isExpanded('Мировые индексы')).toBe(false);
    // Stocks (Акции) parent submenu must remain open
    expect(isExpanded('Акции')).toBe(true);
  });

  it('7. localStorage reflects open/closed after real title clicks', async () => {
    const user = userEvent.setup();
    renderSidebar({ localState: { [LS_STOCKS]: '1', [LS_MI]: '0' } });

    // Initial: closed
    expect(localStorage.getItem(LS_MI)).toBe('0');

    // Open
    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });
    expect(localStorage.getItem(LS_MI)).toBe('1');

    // Close
    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });
    expect(localStorage.getItem(LS_MI)).toBe('0');
  });

  it('7b. localStorage remains "1" after tracked-stock navigation when MI was open', async () => {
    const user = userEvent.setup();
    renderSidebar({ localState: { [LS_STOCKS]: '1', [LS_MI]: '1' } });

    await act(async () => {
      await user.click(screen.getByText('Отслеживаемые акции'));
    });

    expect(localStorage.getItem(LS_MI)).toBe('1');
  });

  it('8. unmount/remount restores persisted open state', async () => {
    const user = userEvent.setup();
    const { unmount } = renderSidebar({ localState: { [LS_STOCKS]: '1', [LS_MI]: '0' } });

    // Open via click — this genuinely saves LS_MI='1' to localStorage
    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });
    expect(localStorage.getItem(LS_MI)).toBe('1');

    unmount();
    cleanup();

    // Remount WITHOUT clearing localStorage — the persisted value must drive initial state
    render(
      <MemoryRouter>
        <LocationCapture />
        <AppSidebar
          portfolios={[]}
          selectedKeys={[]}
          onLogout={() => {}}
          marketIndices={[SP500]}
        />
      </MemoryRouter>,
    );
    expect(isExpanded('Мировые индексы')).toBe(true);
  });

  it('9. /market-indices route forces Market Indices open without overwriting user preference', async () => {
    renderSidebar({
      selectedKeys: ['market-index-1'],
      // No localStorage; component should force open due to route
    });
    expect(isExpanded('Мировые индексы')).toBe(true);
  });

  it('9b. /market-indices management route forces Market Indices open', async () => {
    renderSidebar({ selectedKeys: ['market-indices-manage'] });
    expect(isExpanded('Мировые индексы')).toBe(true);
  });

  it('9c. explicit click closes Market Indices even on active market-index-* route', async () => {
    const user = userEvent.setup();
    // Simulate being on a /market-indices/:id route with submenu open
    renderSidebar({
      selectedKeys: ['market-index-1'],
      localState: { [LS_STOCKS]: '1', [LS_MI]: '1' },
    });

    expect(isExpanded('Мировые индексы')).toBe(true);

    // Explicit click must close it despite the active index route
    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });

    expect(isExpanded('Мировые индексы')).toBe(false);
    // Акции parent must remain open
    expect(isExpanded('Акции')).toBe(true);
    // localStorage must reflect closed state
    expect(localStorage.getItem(LS_MI)).toBe('0');
  });

  it('9d. explicit click closes Market Indices on market-indices-manage route', async () => {
    const user = userEvent.setup();
    renderSidebar({
      selectedKeys: ['market-indices-manage'],
      localState: { [LS_STOCKS]: '1', [LS_MI]: '1' },
    });

    expect(isExpanded('Мировые индексы')).toBe(true);

    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });

    expect(isExpanded('Мировые индексы')).toBe(false);
    expect(localStorage.getItem(LS_MI)).toBe('0');
  });

  it('9e. close on active index route → localStorage=0 → remount keeps it closed', async () => {
    const user = userEvent.setup();
    const { unmount } = renderSidebar({
      selectedKeys: ['market-index-1'],
      localState: { [LS_STOCKS]: '1', [LS_MI]: '1' },
    });

    // Explicit close
    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });
    expect(localStorage.getItem(LS_MI)).toBe('0');

    unmount();
    cleanup();

    // Remount with same route but without manually re-injecting localStorage
    render(
      <MemoryRouter initialEntries={['/market-indices/1']}>
        <LocationCapture />
        <AppSidebar
          portfolios={[]}
          selectedKeys={['market-index-1']}
          onLogout={() => {}}
          marketIndices={[SP500]}
        />
      </MemoryRouter>,
    );
    // Persisted closed preference must win over initial route reveal on remount
    // (initial reveal is one-time; subsequent mounts use the persisted value)
    expect(isExpanded('Мировые индексы')).toBe(false);
  });

  it('9f. after explicit close on active route, next click reopens with all visible entries', async () => {
    const user = userEvent.setup();
    renderSidebar({
      selectedKeys: ['market-index-1'],
      localState: { [LS_STOCKS]: '1', [LS_MI]: '1' },
    });

    // Close
    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });
    expect(isExpanded('Мировые индексы')).toBe(false);

    // Reopen — Управление and SP500 must both appear
    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });
    expect(isExpanded('Мировые индексы')).toBe(true);
    expect(screen.getByText('Управление')).toBeInTheDocument();
    expect(screen.getByText('S&P 500')).toBeInTheDocument();
  });

  it('10. Keyboard interaction: Ant Design submenu titles respond to mouse/pointer but not to keyboard Enter/Space in jsdom', async () => {
    // Ant Design v5 renders submenu title elements that handle click events via
    // pointer/mouse handlers but do not implement keydown/keypress activation in jsdom.
    // Keyboard accessibility is provided by Ant Design's own internal focus management
    // and arrow-key navigation, which relies on native browser focus events not
    // reproducible in jsdom. Testing the actual toggle via userEvent.click (pointer)
    // is verified in tests 1-9f above. This test documents the known jsdom limitation
    // and ensures no uncaught exception is thrown when keyboard events are fired.
    const user = userEvent.setup();
    renderSidebar({ localState: { [LS_STOCKS]: '1', [LS_MI]: '0' } });

    const title = getSubmenuTitle('Мировые индексы');
    title.focus();

    // No uncaught exception must be thrown
    await act(async () => {
      await user.keyboard('{Enter}');
    });

    // Pointer-based click does work and is the primary tested interaction
    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });
    expect(isExpanded('Мировые индексы')).toBe(true);
    expect(screen.getByText('Управление')).toBeInTheDocument();
  });

  it('11. Top-level Справочники section is independent of Market Indices toggle', async () => {
    const user = userEvent.setup();
    renderSidebar({ localState: { [LS_STOCKS]: '1', [LS_MI]: '0', [LS_STOCKS_DIR]: '0' } });

    // Open Справочники
    await act(async () => { await user.click(getSubmenuTitle('Справочники')); });
    expect(isExpanded('Справочники')).toBe(true);
    expect(isExpanded('Мировые индексы')).toBe(false);

    // Open Market Indices — Справочники must remain open
    await act(async () => { await user.click(getSubmenuTitle('Мировые индексы')); });
    expect(isExpanded('Мировые индексы')).toBe(true);
    expect(isExpanded('Справочники')).toBe(true);
  });

  it('12. Mobile: tracked-stock click does not reset Market Indices preference (localStorage check)', async () => {
    // Note: the Drawer (mobile layout) renders the same sidebarContent, so the same
    // Menu component is used. We test via localStorage since jsdom does not exercise
    // the responsive breakpoint. The important guarantee: MI preference is persisted
    // and not cleared by leaf navigation regardless of layout.
    const user = userEvent.setup();
    renderSidebar({ localState: { [LS_STOCKS]: '1', [LS_MI]: '1' } });

    await act(async () => {
      await user.click(screen.getByText('Отслеживаемые акции'));
    });

    // Preference must still be "1" (open) after navigation
    expect(localStorage.getItem(LS_MI)).toBe('1');
  });
});
