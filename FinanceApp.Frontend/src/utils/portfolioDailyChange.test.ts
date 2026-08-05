import { describe, expect, it } from 'vitest';
import type { PortfolioItem, Stock } from '../types';
import {
  computePortfolioDailyChange,
  getDailyChangeColor,
  getPositionDailyChange,
} from './portfolioDailyChange';

const makeItem = (
  id: number,
  quantity: number,
  currentPrice: number,
  currentPriceChange: number | null | undefined,
): PortfolioItem => ({
  id,
  portfolioId: 1,
  stockId: id,
  quantity,
  buyPrice: 1,
  boughtAt: '2026-08-05T00:00:00Z',
  stock: {
    id,
    ticker: `T${id}`,
    name: `Stock ${id}`,
    commonName: `Stock ${id}`,
    exchange: 'NYSE',
    currentPrice,
    currentPriceChange,
    updatedAt: '2026-08-05T00:00:00Z',
  } satisfies Stock,
});

describe('portfolioDailyChange', () => {
  it('multiplies the per-share change by position quantity', () => {
    expect(getPositionDailyChange(makeItem(1, 2.5, 100, 3.2))).toBeCloseTo(8);
    expect(getPositionDailyChange(makeItem(2, 4, 100, -1.5))).toBeCloseTo(-6);
    expect(getPositionDailyChange(makeItem(3, 4, 100, 0))).toBe(0);
  });

  it('keeps unavailable changes unavailable', () => {
    expect(getPositionDailyChange(makeItem(1, 2, 100, null))).toBeNull();
    expect(getPositionDailyChange(makeItem(2, 2, 100, undefined))).toBeNull();
  });

  it('uses positive, negative and neutral colors consistently with Stocks', () => {
    expect(getDailyChangeColor(1)).toBe('#389e0d');
    expect(getDailyChangeColor(-1)).toBe('#cf1322');
    expect(getDailyChangeColor(0)).toBe('#8c8c8c');
    expect(getDailyChangeColor(null)).toBe('#8c8c8c');
  });

  it('computes aggregate euro change and a value-weighted percentage', () => {
    const result = computePortfolioDailyChange([
      makeItem(1, 1, 110, 10), // previous value 100, +10%
      makeItem(2, 9, 210, 10), // previous value 1800, +5%
    ]);

    expect(result.changeEur).toBe(100);
    expect(result.changePercent).toBeCloseTo((100 / 1900) * 100, 10);
    expect(result.changePercent).not.toBeCloseTo(7.5, 2);
  });

  it('excludes positions without daily quote data', () => {
    const result = computePortfolioDailyChange([
      makeItem(1, 2, 105, 5),
      makeItem(2, 100, 999, null),
    ]);

    expect(result.changeEur).toBe(10);
    expect(result.changePercent).toBeCloseTo(5, 10);
  });

  it('returns unavailable aggregates when no position has valid daily data', () => {
    expect(computePortfolioDailyChange([
      makeItem(1, 2, 100, null),
    ])).toEqual({ changeEur: null, changePercent: null });
  });

  it('returns an unavailable percentage when previous-day value is zero', () => {
    expect(computePortfolioDailyChange([
      makeItem(1, 2, 5, 5),
    ])).toEqual({ changeEur: 10, changePercent: null });
  });
});
