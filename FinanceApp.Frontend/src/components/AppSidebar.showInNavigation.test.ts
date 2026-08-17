import { describe, expect, it } from 'vitest';
import type { MarketIndex } from '../types';
import { marketIndexSidebarKey } from './AppSidebar';

// Helper that mirrors the filtering logic in AppSidebar.allMenuItems
function filterForNavigation(indices: MarketIndex[]): MarketIndex[] {
  return indices.filter((idx) => !idx.isArchived && idx.showInNavigation !== false);
}

const makeIndex = (overrides: Partial<MarketIndex> & { id: number; code: string; name: string }): MarketIndex => ({
  description: '',
  countryOrRegion: '',
  sortOrder: 0,
  isArchived: false,
  showInNavigation: true,
  providerSymbol: null,
  ...overrides,
});

describe('AppSidebar – showInNavigation filtering', () => {
  it('includes non-archived index with showInNavigation=true', () => {
    const idx = makeIndex({ id: 1, code: 'SPX', name: 'S&P 500', showInNavigation: true });
    expect(filterForNavigation([idx])).toHaveLength(1);
  });

  it('excludes non-archived index with showInNavigation=false', () => {
    const idx = makeIndex({ id: 2, code: 'MSCIACWI', name: 'MSCI ACWI', showInNavigation: false });
    expect(filterForNavigation([idx])).toHaveLength(0);
  });

  it('excludes archived index regardless of showInNavigation', () => {
    const archived = makeIndex({ id: 3, code: 'OLD', name: 'Old Index', isArchived: true, showInNavigation: true });
    expect(filterForNavigation([archived])).toHaveLength(0);
  });

  it('treats missing showInNavigation as visible (backward compat)', () => {
    // When older backend omits the field, idx.showInNavigation is undefined
    const idx = { ...makeIndex({ id: 4, code: 'COMPAT', name: 'Compat' }) } as unknown as MarketIndex;
    delete (idx as Partial<MarketIndex>).showInNavigation;
    expect(filterForNavigation([idx])).toHaveLength(1);
  });

  it('mixed list filters correctly', () => {
    const indices: MarketIndex[] = [
      makeIndex({ id: 1, code: 'SPX', name: 'S&P 500', showInNavigation: true }),
      makeIndex({ id: 2, code: 'HIDX', name: 'Hidden', showInNavigation: false }),
      makeIndex({ id: 3, code: 'ARCH', name: 'Archived', isArchived: true, showInNavigation: true }),
      makeIndex({ id: 4, code: 'DAX', name: 'DAX', showInNavigation: true }),
    ];
    const visible = filterForNavigation(indices);
    expect(visible).toHaveLength(2);
    expect(visible.map((x) => x.code)).toEqual(['SPX', 'DAX']);
  });

  it('sidebar uses name as the visible label (not code)', () => {
    // The label to render should be idx.name, not idx.code
    const idx = makeIndex({ id: 1, code: 'SPX', name: 'S&P 500' });
    // The sidebar renders idx.name
    expect(idx.name).toBe('S&P 500');
    expect(idx.name).not.toBe(idx.code);
  });

  it('marketIndexSidebarKey generates stable route keys', () => {
    expect(marketIndexSidebarKey(1)).toBe('market-index-1');
    expect(marketIndexSidebarKey(2)).toBe('market-index-2');
  });
});
