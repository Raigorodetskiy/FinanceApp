import { describe, expect, it } from 'vitest';
import { getTransactionDescription } from './PortfolioDetailPage';
import type { Transaction } from '../types';

const baseTransaction = (): Transaction => ({
  id: 1,
  portfolioId: 1,
  type: 'Buy',
  amount: 100,
  signedAmount: -100,
  description: null,
  createdAt: '2024-01-01T00:00:00Z',
  stockId: null,
  stock: null,
  orderId: null,
  instrumentCode: null,
  instrumentCodeType: null,
  quantity: null,
  unitPrice: null,
});

const stockTransaction = (): Transaction => ({
  ...baseTransaction(),
  stockId: 1,
  stock: {
    id: 1,
    ticker: 'AAPL',
    exchange: 'NYSE',
    name: 'Apple Inc.',
    isin: null,
    currency: 'USD',
  },
});

describe('getTransactionDescription', () => {
  it('returns type label when no description and no stock', () => {
    const t = { ...baseTransaction(), type: 'Deposit' as const };
    expect(getTransactionDescription(t)).toBe('Пополнение');
  });

  it('returns generated stock description when no custom description', () => {
    const t = stockTransaction();
    expect(getTransactionDescription(t)).toBe('Покупка — AAPL [NYSE] · Apple Inc.');
  });

  it('returns custom description over generated stock description', () => {
    const t = { ...stockTransaction(), description: 'My custom note' };
    expect(getTransactionDescription(t)).toBe('My custom note');
  });

  it('trims whitespace from custom description', () => {
    const t = { ...stockTransaction(), description: '  trimmed  ' };
    expect(getTransactionDescription(t)).toBe('trimmed');
  });

  it('falls back to generated stock description when description is whitespace-only', () => {
    const t = { ...stockTransaction(), description: '   ' };
    expect(getTransactionDescription(t)).toBe('Покупка — AAPL [NYSE] · Apple Inc.');
  });

  it('falls back to type label when description is empty string and no stock', () => {
    const t = { ...baseTransaction(), description: '', type: 'Sell' as const };
    expect(getTransactionDescription(t)).toBe('Продажа');
  });

  it('returns custom description when stock is absent', () => {
    const t = { ...baseTransaction(), description: 'Cash deposit note' };
    expect(getTransactionDescription(t)).toBe('Cash deposit note');
  });
});
