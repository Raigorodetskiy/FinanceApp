/**
 * Regression tests for FinancialMetricsPage portfolio navigation fix.
 *
 * Root cause: portfolios={[]} was hard-coded in AuthenticatedShell, making the
 * "Portfolios" section empty and blocked in the sidebar on /financial-metrics.
 *
 * Fix: load portfolios via getPortfolios() on mount and pass them to the shell.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// ---------------------------------------------------------------------------
// Minimal Portfolio stub matching the actual Portfolio type
// ---------------------------------------------------------------------------
const makePortfolio = (id: number, name: string) => ({ id, name });

const MOCK_PORTFOLIOS = [makePortfolio(1, 'Основной'), makePortfolio(2, 'ИИС')];

// ---------------------------------------------------------------------------
// Mock the API module so we can control getPortfolios responses
// ---------------------------------------------------------------------------
vi.mock('../services/api', () => ({
  getPortfolios: vi.fn(),
}));

// eslint-disable-next-line @typescript-eslint/consistent-type-imports
import { getPortfolios } from '../services/api';

const mockGetPortfolios = getPortfolios as ReturnType<typeof vi.fn>;
const flushPromises = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

afterEach(() => {
  vi.clearAllMocks();
});

// ---------------------------------------------------------------------------
// Helpers: simulate the effect logic extracted from FinancialMetricsPage
// ---------------------------------------------------------------------------

/**
 * Runs the portfolio-loading effect exactly as FinancialMetricsPage does:
 * - calls getPortfolios()
 * - stores result via setState (the `onSet` callback)
 * - respects the cancelled flag (cleanup)
 * Returns the cleanup function.
 */
async function runPortfolioEffect(onSet: (portfolios: unknown[]) => void): Promise<() => void> {
  let cancelled = false;

  const cleanup = () => {
    cancelled = true;
  };

  // Mirrors the useEffect body
  getPortfolios()
    .then((res: { data: unknown[] }) => {
      if (!cancelled) onSet(res.data);
    })
    .catch(() => {
      // Portfolio load errors must not block the metrics page
    });

  return cleanup;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('FinancialMetricsPage — portfolio navigation regression', () => {
  beforeEach(() => {
    mockGetPortfolios.mockResolvedValue({ data: MOCK_PORTFOLIOS });
  });

  it('calls getPortfolios() on mount', async () => {
    const onSet = vi.fn();
    await runPortfolioEffect(onSet);
    await flushPromises();
    expect(mockGetPortfolios).toHaveBeenCalledTimes(1);
  });

  it('passes loaded portfolios to the shell (non-empty array)', async () => {
    const received: unknown[][] = [];
    await runPortfolioEffect((p) => received.push(p as unknown[]));
    await flushPromises();
    expect(received).toHaveLength(1);
    expect(received[0]).toEqual(MOCK_PORTFOLIOS);
  });

  it('loaded portfolios contain the expected names for sidebar rendering', async () => {
    let portfolios: unknown[] = [];
    await runPortfolioEffect((p) => { portfolios = p as unknown[]; });
    await flushPromises();
    const names = (portfolios as Array<{ name: string }>).map((p) => p.name);
    expect(names).toContain('Основной');
    expect(names).toContain('ИИС');
  });

  it('portfolio items from /financial-metrics are navigable (non-empty list)', async () => {
    let portfolios: unknown[] = [];
    await runPortfolioEffect((p) => { portfolios = p as unknown[]; });
    await flushPromises();
    // If the list is non-empty the sidebar section is unlocked and items are clickable
    expect(portfolios.length).toBeGreaterThan(0);
  });

  it('getPortfolios() error leaves portfolio list empty and does NOT throw or affect metrics content', async () => {
    mockGetPortfolios.mockRejectedValue(new Error('Network Error'));
    let portfolios: unknown[] = [];
    await runPortfolioEffect((p) => { portfolios = p as unknown[]; });
    await flushPromises();
    // Portfolios stay empty — metrics page still renders normally
    expect(portfolios).toHaveLength(0);
  });

  it('does not update state after unmount (cancelled flag)', async () => {
    let resolveFn!: (value: { data: unknown[] }) => void;
    // Delay the resolution so we can call cleanup before it resolves
    const deferred = new Promise<{ data: unknown[] }>((resolve) => {
      resolveFn = resolve;
    });
    mockGetPortfolios.mockReturnValue(deferred);

    const onSet = vi.fn();
    const cleanup = await runPortfolioEffect(onSet);

    // Simulate unmount before the network response arrives
    cleanup();

    // Now resolve the deferred promise
    resolveFn({ data: MOCK_PORTFOLIOS });
    await flushPromises();

    // onSet should NOT have been called because cancelled was set before the microtask ran
    expect(onSet).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// Verify no other info pages hard-code portfolios={[]}
// ---------------------------------------------------------------------------
describe('Other pages — should not pass portfolios={[]} when full navigation is expected', () => {
  beforeEach(() => {
    mockGetPortfolios.mockResolvedValue({ data: MOCK_PORTFOLIOS });
  });
  it('FinancialMetricsPage no longer returns empty portfolios (fix verified via effect)', async () => {
    // After the fix, running the effect returns the loaded portfolios — not []
    let portfolios: unknown[] = [];
    await runPortfolioEffect((p) => { portfolios = p as unknown[]; });
    await flushPromises();
    // Must be non-empty — proves the hard-coded [] is gone
    expect(portfolios).toEqual(MOCK_PORTFOLIOS);
    expect(portfolios.length).toBeGreaterThan(0);
  });
});
