import { describe, expect, it } from 'vitest';
import dayjs from 'dayjs';
import { filterTransactions } from './PortfolioDetailPage';
import type { Transaction } from '../types';

const makeTransaction = (overrides: Partial<Transaction> = {}): Transaction => ({
  id: 1,
  portfolioId: 1,
  type: 'Buy',
  amount: 100,
  signedAmount: -100,
  description: null,
  createdAt: '2025-06-15T12:00:00Z',
  stockId: null,
  stock: null,
  orderId: null,
  instrumentCode: null,
  instrumentCodeType: null,
  quantity: null,
  unitPrice: null,
  ...overrides,
});

const makeStock = (overrides: Partial<NonNullable<Transaction['stock']>> = {}): NonNullable<Transaction['stock']> => ({
  id: 1,
  ticker: 'NVDA',
  name: 'NVIDIA CORP.',
  commonName: 'Nvidia',
  exchange: 'NASDAQ' as never,
  currentPrice: 100,
  updatedAt: '2025-06-15T12:00:00Z',
  wkn: 'A14Y6F',
  isin: 'US67066G1040',
  ...overrides,
});

const ALL: Transaction[] = [
  makeTransaction({ id: 1, type: 'Buy', createdAt: '2025-01-10T12:00:00', description: 'Brokerimport; Kauf; NVIDIA CORP.' }),
  makeTransaction({ id: 2, type: 'Sell', createdAt: '2025-03-20T15:00:00', description: null, stock: makeStock() }),
  makeTransaction({ id: 3, type: 'Deposit', createdAt: '2025-06-01T08:00:00', description: 'Cash deposit' }),
  makeTransaction({ id: 4, type: 'Buy', createdAt: '2025-06-30T20:00:00', description: 'Brokerimport; Kauf; RHEINMETALL AG; ISIN=DE0007030009', stock: makeStock({ ticker: 'RHM', name: 'RHEINMETALL AG', commonName: 'Rheinmetall', isin: 'DE0007030009', wkn: '703000' }) }),
];

describe('filterTransactions – type filter', () => {
  it('returns all transactions for type=all', () => {
    expect(filterTransactions(ALL, 'all', null, null, '')).toHaveLength(ALL.length);
  });

  it('filters by type=Buy', () => {
    const result = filterTransactions(ALL, 'Buy', null, null, '');
    expect(result.every((t) => t.type === 'Buy')).toBe(true);
    expect(result).toHaveLength(2);
  });

  it('filters by type=Deposit', () => {
    const result = filterTransactions(ALL, 'Deposit', null, null, '');
    expect(result).toHaveLength(1);
    expect(result[0].id).toBe(3);
  });
});

describe('filterTransactions – date range', () => {
  it('filters by dateFrom (inclusive start of day)', () => {
    const from = dayjs('2025-03-20');
    const result = filterTransactions(ALL, 'all', from, null, '');
    const ids = result.map((t) => t.id).sort();
    expect(ids).toEqual([2, 3, 4]);
  });

  it('filters by dateTo (inclusive end of day)', () => {
    const to = dayjs('2025-03-20');
    const result = filterTransactions(ALL, 'all', null, to, '');
    const ids = result.map((t) => t.id).sort();
    expect(ids).toEqual([1, 2]);
  });

  it('inclusive: transaction on dateTo day is included regardless of time', () => {
    // tx id=4 has createdAt '2025-06-30T23:59:59Z'
    const to = dayjs('2025-06-30');
    const result = filterTransactions(ALL, 'all', null, to, '');
    expect(result.some((t) => t.id === 4)).toBe(true);
  });

  it('filters both dateFrom and dateTo (AND)', () => {
    const from = dayjs('2025-03-01');
    const to = dayjs('2025-06-15');
    const result = filterTransactions(ALL, 'all', from, to, '');
    const ids = result.map((t) => t.id).sort();
    expect(ids).toEqual([2, 3]);
  });

  it('null dates impose no restriction', () => {
    expect(filterTransactions(ALL, 'all', null, null, '')).toHaveLength(ALL.length);
  });
});

describe('filterTransactions – text search', () => {
  it('matches persisted description case-insensitively', () => {
    const result = filterTransactions(ALL, 'all', null, null, 'nvidia');
    expect(result.some((t) => t.id === 1)).toBe(true);
  });

  it('matches stock ticker', () => {
    const result = filterTransactions(ALL, 'all', null, null, 'nvda');
    expect(result.some((t) => t.id === 2)).toBe(true);
  });

  it('matches stock name', () => {
    const result = filterTransactions(ALL, 'all', null, null, 'nvidia corp');
    // id=1 has description, id=2 has stock with that name
    expect(result.length).toBeGreaterThanOrEqual(1);
  });

  it('matches stock commonName', () => {
    const result = filterTransactions(ALL, 'all', null, null, 'rheinmetall');
    expect(result.some((t) => t.id === 4)).toBe(true);
  });

  it('matches ISIN', () => {
    const result = filterTransactions(ALL, 'all', null, null, 'DE0007030009');
    expect(result.some((t) => t.id === 4)).toBe(true);
  });

  it('matches WKN', () => {
    const result = filterTransactions(ALL, 'all', null, null, '703000');
    expect(result.some((t) => t.id === 4)).toBe(true);
  });

  it('matches localized type label', () => {
    // 'Покупка' should match Buy transactions
    const result = filterTransactions(ALL, 'all', null, null, 'Покупка');
    expect(result.every((t) => t.type === 'Buy')).toBe(true);
    expect(result.length).toBeGreaterThan(0);
  });

  it('empty query returns all transactions', () => {
    expect(filterTransactions(ALL, 'all', null, null, '')).toHaveLength(ALL.length);
    expect(filterTransactions(ALL, 'all', null, null, '   ')).toHaveLength(ALL.length);
  });

  it('returns empty when no match', () => {
    expect(filterTransactions(ALL, 'all', null, null, 'xyznoexist')).toHaveLength(0);
  });
});

describe('filterTransactions – combined AND semantics', () => {
  it('combines type + text filters', () => {
    const result = filterTransactions(ALL, 'Buy', null, null, 'rheinmetall');
    expect(result).toHaveLength(1);
    expect(result[0].id).toBe(4);
  });

  it('combines type + date + text filters', () => {
    const from = dayjs('2025-06-01');
    const to = dayjs('2025-06-30');
    const result = filterTransactions(ALL, 'Buy', from, to, 'rheinmetall');
    expect(result).toHaveLength(1);
    expect(result[0].id).toBe(4);
  });

  it('does not mutate the original array', () => {
    const original = [...ALL];
    filterTransactions(ALL, 'Buy', dayjs('2025-01-01'), dayjs('2025-03-01'), 'nvidia');
    expect(ALL).toHaveLength(original.length);
  });
});
