import { describe, expect, it, vi } from 'vitest';
import { loadMarketIndicesPagePortfolios, matchesMarketIndexSearch, MARKET_INDICES_SELECTED_KEY } from './MarketIndicesPage';
import type { MarketIndex } from '../types';

const sampleIndex: MarketIndex = {
  id: 1,
  code: 'SPX',
  name: 'S&P 500',
  description: 'Эталон рынка США',
  countryOrRegion: 'USA',
  sortOrder: 10,
  isArchived: false,
  showInNavigation: true,
};

describe('MarketIndicesPage helpers', () => {
  it('searches by code, name, country and description case-insensitively', () => {
    expect(matchesMarketIndexSearch(sampleIndex, 'spx')).toBe(true);
    expect(matchesMarketIndexSearch(sampleIndex, 'рынка')).toBe(true);
    expect(matchesMarketIndexSearch(sampleIndex, 'usa')).toBe(true);
    expect(matchesMarketIndexSearch(sampleIndex, '500')).toBe(true);
    expect(matchesMarketIndexSearch(sampleIndex, 'nasdaq')).toBe(false);
  });

  it('loads portfolios for sidebar navigation', async () => {
    const loadPortfolios = vi.fn().mockResolvedValue({ data: [{ id: 1, name: 'Основной' }] });
    await expect(loadMarketIndicesPagePortfolios(loadPortfolios)).resolves.toEqual([{ id: 1, name: 'Основной' }]);
  });

  it('portfolio loading errors do not break the page', async () => {
    const loadPortfolios = vi.fn().mockRejectedValue(new Error('boom'));
    await expect(loadMarketIndicesPagePortfolios(loadPortfolios)).resolves.toEqual([]);
  });

  it('uses market-indices-manage as the active sidebar key for the overview page', () => {
    expect(MARKET_INDICES_SELECTED_KEY).toBe('market-indices-manage');
  });
});
