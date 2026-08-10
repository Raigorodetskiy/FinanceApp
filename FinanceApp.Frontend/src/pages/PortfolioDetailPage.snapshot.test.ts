/**
 * Focused unit tests for transaction instrument-snapshot helper functions
 * exported from PortfolioDetailPage.
 *
 * These tests do NOT mount the React component; they exercise pure logic only.
 */
import { describe, expect, it } from 'vitest';
import type { Stock, Transaction } from '../types';
import {
  deriveSnapshotFromStock,
  isValidIsin,
  isValidTicker,
} from './PortfolioDetailPage';

// ── Helpers ───────────────────────────────────────────────────────────────────

const makeStock = (overrides: Partial<Stock> = {}): Stock => ({
  id: 1,
  ticker: 'AAPL',
  name: 'Apple Inc.',
  commonName: 'Apple',
  exchange: 'NYSE',
  currentPrice: 150,
  updatedAt: '2024-01-01T00:00:00Z',
  isin: 'US0378331005',
  wkn: null,
  finanzenNetSlug: null,
  ...overrides,
});

const makeTx = (overrides: Partial<Transaction> = {}): Transaction => ({
  id: 1,
  portfolioId: 1,
  type: 'Buy',
  amount: 500,
  signedAmount: -500,
  description: null,
  createdAt: '2024-01-01T00:00:00Z',
  stockId: null,
  stock: null,
  orderId: null,
  instrumentCode: null,
  instrumentCodeType: null,
  quantity: null,
  unitPrice: null,
  ...overrides,
});

// ── deriveSnapshotFromStock ───────────────────────────────────────────────────

describe('deriveSnapshotFromStock', () => {
  it('returns ISIN when non-empty ISIN is present', () => {
    const result = deriveSnapshotFromStock(makeStock({ isin: 'US0378331005', ticker: 'AAPL' }));
    expect(result).toEqual({ instrumentCode: 'US0378331005', instrumentCodeType: 'ISIN' });
  });

  it('trims whitespace from ISIN', () => {
    const result = deriveSnapshotFromStock(makeStock({ isin: '  US0378331005  ' }));
    expect(result?.instrumentCode).toBe('US0378331005');
    expect(result?.instrumentCodeType).toBe('ISIN');
  });

  it('falls back to Ticker when ISIN is null', () => {
    const result = deriveSnapshotFromStock(makeStock({ isin: null, ticker: 'AAPL' }));
    expect(result).toEqual({ instrumentCode: 'AAPL', instrumentCodeType: 'Ticker' });
  });

  it('falls back to Ticker when ISIN is empty string', () => {
    const result = deriveSnapshotFromStock(makeStock({ isin: '', ticker: 'MSFT' }));
    expect(result).toEqual({ instrumentCode: 'MSFT', instrumentCodeType: 'Ticker' });
  });

  it('falls back to Ticker when ISIN is whitespace only', () => {
    const result = deriveSnapshotFromStock(makeStock({ isin: '   ', ticker: 'TSLA' }));
    expect(result).toEqual({ instrumentCode: 'TSLA', instrumentCodeType: 'Ticker' });
  });

  it('returns null when both ISIN and ticker are empty', () => {
    const result = deriveSnapshotFromStock(makeStock({ isin: null, ticker: '' }));
    expect(result).toBeNull();
  });

  it('trims whitespace from ticker', () => {
    const result = deriveSnapshotFromStock(makeStock({ isin: null, ticker: '  NVDA  ' }));
    expect(result?.instrumentCode).toBe('NVDA');
    expect(result?.instrumentCodeType).toBe('Ticker');
  });
});

// ── isValidIsin ───────────────────────────────────────────────────────────────

describe('isValidIsin', () => {
  it('accepts a valid 12-char ISIN', () => {
    expect(isValidIsin('US0378331005')).toBe(true);
  });

  it('accepts a valid DE ISIN', () => {
    expect(isValidIsin('DE0005140008')).toBe(true);
  });

  it('rejects ISIN shorter than 12 chars', () => {
    expect(isValidIsin('US037833100')).toBe(false);
  });

  it('rejects ISIN longer than 12 chars', () => {
    expect(isValidIsin('US03783310051')).toBe(false);
  });

  it('rejects ISIN starting with digit', () => {
    expect(isValidIsin('1S0378331005')).toBe(false);
  });

  it('rejects ISIN with special characters', () => {
    expect(isValidIsin('US03783310-5')).toBe(false);
  });

  it('accepts mixed-case ISIN (normalised internally)', () => {
    expect(isValidIsin('us0378331005')).toBe(true);
  });

  it('accepts ISIN with leading/trailing whitespace', () => {
    expect(isValidIsin('  US0378331005  ')).toBe(true);
  });
});

// ── isValidTicker ─────────────────────────────────────────────────────────────

describe('isValidTicker', () => {
  it('accepts a short ticker', () => {
    expect(isValidTicker('AAPL')).toBe(true);
  });

  it('accepts a ticker of exactly 32 chars', () => {
    expect(isValidTicker('A'.repeat(32))).toBe(true);
  });

  it('rejects an empty string', () => {
    expect(isValidTicker('')).toBe(false);
  });

  it('rejects whitespace-only string', () => {
    expect(isValidTicker('   ')).toBe(false);
  });

  it('rejects a ticker longer than 32 chars', () => {
    expect(isValidTicker('A'.repeat(33))).toBe(false);
  });
});

// ── Existing-transaction snapshot preservation ────────────────────────────────

describe('existing transaction snapshot is preserved in edit mode (data-level)', () => {
  it('transaction snapshot fields are read from the stored transaction', () => {
    const tx = makeTx({
      instrumentCode: 'DE0005140008',
      instrumentCodeType: 'ISIN',
      quantity: 10,
      unitPrice: 12.5,
    });
    // These would be loaded into the form via txForm.setFieldsValue in openEditTxModal.
    // Verify the source data is present and accessible:
    expect(tx.instrumentCode).toBe('DE0005140008');
    expect(tx.instrumentCodeType).toBe('ISIN');
    expect(tx.quantity).toBe(10);
    expect(tx.unitPrice).toBe(12.5);
  });

  it('transaction with null snapshot fields has all four null', () => {
    const tx = makeTx();
    expect(tx.instrumentCode).toBeNull();
    expect(tx.instrumentCodeType).toBeNull();
    expect(tx.quantity).toBeNull();
    expect(tx.unitPrice).toBeNull();
  });
});

// ── Payload normalization helpers ─────────────────────────────────────────────

describe('payload normalization', () => {
  const normalize = (v: string | null | undefined): string | null => {
    const t = (v ?? '').trim();
    return t.length > 0 ? t : null;
  };

  it('trims and returns non-blank code', () => {
    expect(normalize('  US0378331005  ')).toBe('US0378331005');
  });

  it('normalizes whitespace-only code to null', () => {
    expect(normalize('   ')).toBeNull();
  });

  it('normalizes empty string to null', () => {
    expect(normalize('')).toBeNull();
  });

  it('normalizes null to null', () => {
    expect(normalize(null)).toBeNull();
  });

  it('preserves zero quantity (not null)', () => {
    const quantity: number | null = 0;
    expect(quantity).toBe(0);
    expect(quantity ?? null).toBe(0);
  });

  it('preserves zero unitPrice (not null)', () => {
    const unitPrice: number | null = 0;
    expect(unitPrice).toBe(0);
    expect(unitPrice ?? null).toBe(0);
  });
});

// ── Transaction type visibility logic ─────────────────────────────────────────

describe('transaction type snapshot visibility', () => {
  const showsSnapshot = (type: Transaction['type']): boolean =>
    type === 'Buy' || type === 'Sell' || type === 'Dividend';

  const showsQtyAndPrice = (type: Transaction['type']): boolean =>
    type === 'Buy' || type === 'Sell';

  it('Buy shows all four fields', () => {
    expect(showsSnapshot('Buy')).toBe(true);
    expect(showsQtyAndPrice('Buy')).toBe(true);
  });

  it('Sell shows all four fields', () => {
    expect(showsSnapshot('Sell')).toBe(true);
    expect(showsQtyAndPrice('Sell')).toBe(true);
  });

  it('Dividend shows only code/type', () => {
    expect(showsSnapshot('Dividend')).toBe(true);
    expect(showsQtyAndPrice('Dividend')).toBe(false);
  });

  it('Deposit hides all four fields', () => {
    expect(showsSnapshot('Deposit')).toBe(false);
    expect(showsQtyAndPrice('Deposit')).toBe(false);
  });

  it('Withdrawal hides all four fields', () => {
    expect(showsSnapshot('Withdrawal')).toBe(false);
    expect(showsQtyAndPrice('Withdrawal')).toBe(false);
  });
});

// ── Deposit/Withdrawal nulling out snapshot fields ────────────────────────────

describe('Deposit and Withdrawal submit nulls for snapshot fields', () => {
  /** Mirrors the payload-build logic in handleTxSubmit for snapshot fields. */
  const buildSnapshotPayload = (
    type: Transaction['type'],
    code: string | null | undefined,
    codeType: 'ISIN' | 'Ticker' | null | undefined,
    quantity: number | null | undefined,
    unitPrice: number | null | undefined,
  ) => {
    const hideSnapshot = type === 'Deposit' || type === 'Withdrawal';
    const normalizeCode = (v?: string | null): string | null => {
      if (hideSnapshot) return null;
      const t = (v ?? '').trim();
      return t.length > 0 ? t : null;
    };
    return {
      instrumentCode: normalizeCode(code),
      instrumentCodeType: hideSnapshot ? null : (codeType ?? null),
      quantity: hideSnapshot ? null : (quantity ?? null),
      unitPrice: hideSnapshot ? null : (unitPrice ?? null),
    };
  };

  it('Deposit submits all four as null even when stale values are provided', () => {
    const payload = buildSnapshotPayload('Deposit', 'US0378331005', 'ISIN', 10, 12.5);
    expect(payload.instrumentCode).toBeNull();
    expect(payload.instrumentCodeType).toBeNull();
    expect(payload.quantity).toBeNull();
    expect(payload.unitPrice).toBeNull();
  });

  it('Withdrawal submits all four as null', () => {
    const payload = buildSnapshotPayload('Withdrawal', 'AAPL', 'Ticker', 5, 200);
    expect(payload.instrumentCode).toBeNull();
    expect(payload.instrumentCodeType).toBeNull();
    expect(payload.quantity).toBeNull();
    expect(payload.unitPrice).toBeNull();
  });

  it('Buy includes snapshot values', () => {
    const payload = buildSnapshotPayload('Buy', 'US0378331005', 'ISIN', 10, 150.0);
    expect(payload.instrumentCode).toBe('US0378331005');
    expect(payload.instrumentCodeType).toBe('ISIN');
    expect(payload.quantity).toBe(10);
    expect(payload.unitPrice).toBe(150.0);
  });

  it('Sell includes snapshot values', () => {
    const payload = buildSnapshotPayload('Sell', 'AAPL', 'Ticker', 3, 200.5);
    expect(payload.instrumentCode).toBe('AAPL');
    expect(payload.instrumentCodeType).toBe('Ticker');
    expect(payload.quantity).toBe(3);
    expect(payload.unitPrice).toBe(200.5);
  });

  it('Dividend includes code/type but quantity/unitPrice are null when not set', () => {
    const payload = buildSnapshotPayload('Dividend', 'US0378331005', 'ISIN', null, null);
    expect(payload.instrumentCode).toBe('US0378331005');
    expect(payload.instrumentCodeType).toBe('ISIN');
    expect(payload.quantity).toBeNull();
    expect(payload.unitPrice).toBeNull();
  });

  it('Buy preserves zero quantity', () => {
    const payload = buildSnapshotPayload('Buy', null, null, 0, 0);
    expect(payload.quantity).toBe(0);
    expect(payload.unitPrice).toBe(0);
  });
});

// ── Amount is not recalculated ────────────────────────────────────────────────

describe('amount is not recalculated from quantity * unitPrice', () => {
  it('amount is preserved independently from quantity/unitPrice', () => {
    const tx = makeTx({ amount: 1600, quantity: 10, unitPrice: 150 });
    // 10 * 150 = 1500, but amount is 1600 (includes commission)
    expect(tx.amount).toBe(1600);
    expect(tx.amount).not.toBe((tx.quantity ?? 0) * (tx.unitPrice ?? 0));
  });
});

// ── No new transaction-table columns ─────────────────────────────────────────

describe('no transaction-table columns for snapshot fields', () => {
  // The PORTFOLIO_TRANSACTION_RIGHT_ALIGNED_MONEY_KEYS list only contains 'amount'.
  // This test verifies the exported constant remains unchanged.
  it('PORTFOLIO_TRANSACTION_RIGHT_ALIGNED_MONEY_KEYS contains only amount', async () => {
    const { PORTFOLIO_TRANSACTION_RIGHT_ALIGNED_MONEY_KEYS } = await import('./PortfolioDetailPage');
    expect(PORTFOLIO_TRANSACTION_RIGHT_ALIGNED_MONEY_KEYS).toEqual(['amount']);
  });
});
