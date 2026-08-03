import { describe, expect, it } from 'vitest';
import { formatCurrency, formatPercent } from './currency';

describe('formatCurrency', () => {
  it('places the symbol after the number', () => {
    expect(formatCurrency(458.92, '€')).toBe('458,92 €');
  });

  it('formats positive value without sign by default', () => {
    expect(formatCurrency(12.34, '€')).toBe('12,34 €');
  });

  it('formats positive value with explicit plus when signed=true', () => {
    expect(formatCurrency(12.34, '€', { signed: true })).toBe('+12,34 €');
  });

  it('formats negative value with minus before the digits', () => {
    expect(formatCurrency(-12.34, '€')).toBe('-12,34 €');
  });

  it('formats negative value with minus (signed=true does not add extra sign)', () => {
    expect(formatCurrency(-12.34, '€', { signed: true })).toBe('-12,34 €');
  });

  it('formats zero without sign by default', () => {
    expect(formatCurrency(0, '€')).toBe('0,00 €');
  });

  it('formats zero without plus sign even when signed=true', () => {
    expect(formatCurrency(0, '€', { signed: true })).toBe('0,00 €');
  });

  it('returns em dash for null', () => {
    expect(formatCurrency(null, '€')).toBe('—');
  });

  it('returns em dash for undefined', () => {
    expect(formatCurrency(undefined, '€')).toBe('—');
  });

  it('returns em dash for NaN', () => {
    expect(formatCurrency(NaN, '€')).toBe('—');
  });

  it('does not produce €- or +- patterns', () => {
    const neg = formatCurrency(-5, '€', { signed: true });
    expect(neg).not.toContain('€-');
    expect(neg).not.toContain('+-');
    expect(neg).toBe('-5,00 €');
  });

  it('works with non-EUR symbols', () => {
    expect(formatCurrency(123.45, 'USD')).toBe('123,45 USD');
  });

  it('respects custom decimal places', () => {
    expect(formatCurrency(1.1, '€', { decimals: 4 })).toBe('1,1000 €');
  });
});

describe('formatPercent', () => {
  it('formats positive percentage with plus sign', () => {
    expect(formatPercent(1.23)).toBe('+1,23 %');
  });

  it('formats negative percentage with minus sign', () => {
    expect(formatPercent(-1.23)).toBe('-1,23 %');
  });

  it('formats zero without sign', () => {
    expect(formatPercent(0)).toBe('0,00 %');
  });

  it('returns em dash for null', () => {
    expect(formatPercent(null)).toBe('—');
  });

  it('returns em dash for undefined', () => {
    expect(formatPercent(undefined)).toBe('—');
  });
});
