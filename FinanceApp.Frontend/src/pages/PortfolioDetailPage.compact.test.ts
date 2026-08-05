import { describe, expect, it } from 'vitest';
import {
  PORTFOLIO_EXECUTED_ORDER_RIGHT_ALIGNED_MONEY_KEYS,
  PORTFOLIO_PENDING_ORDER_RIGHT_ALIGNED_MONEY_KEYS,
  PORTFOLIO_POSITION_RIGHT_ALIGNED_MONEY_KEYS,
  PORTFOLIO_TRANSACTION_RIGHT_ALIGNED_MONEY_KEYS,
  SUMMARY_CARD_SIZE,
  SUMMARY_ROW_GUTTER,
  SUMMARY_ROW_MARGIN_BOTTOM,
} from './PortfolioDetailPage';

describe('PortfolioDetailPage – compact summary cards', () => {
  it('uses small Ant Design Card size for summary cards', () => {
    expect(SUMMARY_CARD_SIZE).toBe('small');
  });

  it('uses reduced vertical gutter (8 px) between summary rows', () => {
    expect(SUMMARY_ROW_GUTTER[1]).toBe(8);
  });

  it('keeps horizontal gutter unchanged at 16 px', () => {
    expect(SUMMARY_ROW_GUTTER[0]).toBe(16);
  });

  it('reduces bottom margin of summary section to 12 px (was 24 px)', () => {
    expect(SUMMARY_ROW_MARGIN_BOTTOM).toBe(12);
    expect(SUMMARY_ROW_MARGIN_BOTTOM).toBeLessThan(24);
  });
});


describe('PortfolioDetailPage – right-aligned monetary columns', () => {
  it('right-aligns monetary columns in portfolio positions only', () => {
    expect([...PORTFOLIO_POSITION_RIGHT_ALIGNED_MONEY_KEYS]).toEqual([
      'buyPrice',
      'currentPrice',
      'dailyPriceChange',
      'currentValue',
      'dailyPositionChange',
      'pnlEur',
    ]);
    expect(PORTFOLIO_POSITION_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('quantity');
    expect(PORTFOLIO_POSITION_RIGHT_ALIGNED_MONEY_KEYS).not.toContain('pnlPct');
  });

  it('right-aligns only actual price columns in pending and executed orders', () => {
    expect([...PORTFOLIO_PENDING_ORDER_RIGHT_ALIGNED_MONEY_KEYS]).toEqual([
      'price',
      'stopLoss',
      'stopMarket',
      'currentPrice',
    ]);
    expect([...PORTFOLIO_EXECUTED_ORDER_RIGHT_ALIGNED_MONEY_KEYS]).toEqual(['price', 'total']);
  });

  it('right-aligns the transaction amount column only', () => {
    expect([...PORTFOLIO_TRANSACTION_RIGHT_ALIGNED_MONEY_KEYS]).toEqual(['amount']);
  });
});
