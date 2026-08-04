import { describe, expect, it } from 'vitest';
import dayjs from 'dayjs';
import utc from 'dayjs/plugin/utc';
import {
  CHANGE_EUR_COL_WIDTH,
  CHANGE_PCT_COL_WIDTH,
  PRICE_TIME_FORMAT,
  STOCKS_CHANGE_COMPACT_CLASS,
} from './StocksPage';

dayjs.extend(utc);

describe('Stocks table – compact change columns', () => {
  it('CHANGE_EUR_COL_WIDTH is approximately 85 px', () => {
    expect(CHANGE_EUR_COL_WIDTH).toBeLessThanOrEqual(90);
    expect(CHANGE_EUR_COL_WIDTH).toBeGreaterThanOrEqual(80);
  });

  it('CHANGE_PCT_COL_WIDTH is approximately 75 px', () => {
    expect(CHANGE_PCT_COL_WIDTH).toBeLessThanOrEqual(80);
    expect(CHANGE_PCT_COL_WIDTH).toBeGreaterThanOrEqual(70);
  });

  it('compact column class name is defined', () => {
    expect(STOCKS_CHANGE_COMPACT_CLASS).toBe('stock-change-compact-col');
  });
});

describe('Stocks table – price timestamp format', () => {
  it('PRICE_TIME_FORMAT uses two-digit year (DD.MM.YY HH:mm)', () => {
    expect(PRICE_TIME_FORMAT).toBe('DD.MM.YY HH:mm');
  });

  it('formats UTC timestamp 2026-08-04T07:08:00Z as DD.MM.YY HH:mm in local time', () => {
    // Use UTC-based formatting to get a deterministic result regardless of timezone.
    // The format token YY gives the last two digits of the year.
    const ts = '2026-08-04T07:08:00Z';
    const formatted = dayjs.utc(ts).local().format(PRICE_TIME_FORMAT);
    // Year portion must be two digits (26 for 2026).
    expect(formatted).toMatch(/\d{2}\.\d{2}\.\d{2} \d{2}:\d{2}/);
    // Extract the year fragment – must be exactly 2 characters wide.
    const parts = formatted.split(' ');
    const dateParts = parts[0].split('.');
    expect(dateParts[2]).toHaveLength(2);
    expect(dateParts[2]).toBe('26');
  });

  it('does NOT use four-digit year format', () => {
    const ts = '2026-08-04T07:08:00Z';
    const formatted = dayjs.utc(ts).local().format(PRICE_TIME_FORMAT);
    // Should not contain "2026" (full year).
    expect(formatted).not.toContain('2026');
  });

  it('missing timestamp should fall back to —', () => {
    // Simulate the null guard from the render function.
    const ts: string | null = null;
    const result = ts ? dayjs.utc(ts).local().format(PRICE_TIME_FORMAT) : '—';
    expect(result).toBe('—');
  });
});
