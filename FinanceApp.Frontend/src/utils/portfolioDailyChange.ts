import type { PortfolioItem } from '../types';

export type PortfolioDailyChangeSummary = {
  changeEur: number | null;
  changePercent: number | null;
};

export const getDailyChangeColor = (value: number | null | undefined): string =>
  value == null || !Number.isFinite(value) || value === 0
    ? '#8c8c8c'
    : value > 0
      ? '#389e0d'
      : '#cf1322';

export const getPositionDailyChange = (item: PortfolioItem): number | null => {
  const change = item.stock?.currentPriceChange;
  if (change == null || !Number.isFinite(change) || !Number.isFinite(item.quantity)) return null;
  return change * item.quantity;
};

export const computePortfolioDailyChange = (
  items: PortfolioItem[],
): PortfolioDailyChangeSummary => {
  let changeEur = 0;
  let currentIncludedValue = 0;
  let validPositionCount = 0;

  for (const item of items) {
    const positionChange = getPositionDailyChange(item);
    const currentPrice = item.stock?.currentPrice;
    if (
      positionChange == null
      || currentPrice == null
      || !Number.isFinite(currentPrice)
      || !Number.isFinite(item.quantity)
    ) {
      continue;
    }

    changeEur += positionChange;
    currentIncludedValue += currentPrice * item.quantity;
    validPositionCount += 1;
  }

  if (validPositionCount === 0) {
    return { changeEur: null, changePercent: null };
  }

  const previousIncludedValue = currentIncludedValue - changeEur;
  const changePercent = Number.isFinite(previousIncludedValue) && previousIncludedValue !== 0
    ? (changeEur / previousIncludedValue) * 100
    : null;

  return { changeEur, changePercent };
};
