import { describe, expect, it } from 'vitest';
import {
  FUNDAMENTALS_EMPTY_TEXT,
  canRefreshFundamentals,
  formatCompactFinancialValue,
  getEarningsStatusBadgeProps,
  getFundamentalsNumberDisplay,
  getFundamentalsRefreshWarningMessage,
  shouldDiscardFundamentalsResponse,
  shouldShowFundamentalsRefreshSuccess,
} from './StockFundamentalsDrawer';

describe('StockFundamentalsDrawer helpers', () => {
  it('displays null values as em dash', () => {
    expect(getFundamentalsNumberDisplay(null)).toBe(FUNDAMENTALS_EMPTY_TEXT);
    expect(formatCompactFinancialValue(undefined)).toBe(FUNDAMENTALS_EMPTY_TEXT);
  });

  it('formats large values compactly', () => {
    expect(formatCompactFinancialValue(1_250_000_000_000)).toBe('1.3T');
    expect(formatCompactFinancialValue(2_500_000_000, 'USD')).toBe('2.5B USD');
    expect(formatCompactFinancialValue(15_000_000)).toBe('15.0M');
  });

  it('returns correct earnings status badges', () => {
    expect(getEarningsStatusBadgeProps('Estimated')).toEqual({ color: 'gold', text: 'Ожидаемая' });
    expect(getEarningsStatusBadgeProps('Confirmed')).toEqual({ color: 'green', text: 'Подтверждённая' });
    expect(getEarningsStatusBadgeProps('Unknown')).toEqual({ color: 'default', text: 'Статус неизвестен' });
  });

  it('disables refresh without stock or while refreshing', () => {
    expect(canRefreshFundamentals(null, false)).toBe(false);
    expect(canRefreshFundamentals({ id: 5 }, true)).toBe(false);
    expect(canRefreshFundamentals({ id: 5 }, false)).toBe(true);
  });

  it('shows success toast only for Fresh state with non-null snapshot', () => {
    expect(shouldShowFundamentalsRefreshSuccess({
      stockId: 1,
      state: 'Fresh',
      warningMessage: null,
      snapshot: {} as any,
      periods: [],
      earningsEvents: [],
    })).toBe(true);

    expect(shouldShowFundamentalsRefreshSuccess({
      stockId: 1,
      state: 'Unavailable',
      warningMessage: 'provider failed',
      snapshot: null,
      periods: [],
      earningsEvents: [],
    })).toBe(false);
  });

  it('returns warning text for stale and unavailable refresh responses', () => {
    expect(getFundamentalsRefreshWarningMessage({
      stockId: 1,
      state: 'Stale',
      warningMessage: null,
      snapshot: {} as any,
      periods: [],
      earningsEvents: [],
    })).toContain('Показан сохранённый снимок');

    expect(getFundamentalsRefreshWarningMessage({
      stockId: 1,
      state: 'Unavailable',
      warningMessage: null,
      snapshot: null,
      periods: [],
      earningsEvents: [],
    })).toBe('Не удалось загрузить фундаментальные данные.');
  });

  it('discards outdated request results when stock changes', () => {
    expect(shouldDiscardFundamentalsResponse(3, 4)).toBe(true);
    expect(shouldDiscardFundamentalsResponse(7, 7)).toBe(false);
  });
});
