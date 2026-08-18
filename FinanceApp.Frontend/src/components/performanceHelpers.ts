import type { IndexConstituentDto } from '../types';

/** Map from stockId → changePercent (null = insufficient data / missing). */
export type PerformanceMap = ReadonlyMap<number, number | null>;

export type PerformanceDisplayValue =
  | { kind: 'available'; changePercent: number; formatted: string; color: string }
  | { kind: 'unavailable' };

const COLOR_POSITIVE = '#389e0d';
const COLOR_NEGATIVE = '#cf1322';
export const NAME_SORT_COLLATOR_LOCALE = 'en';
export const NAME_SORT_COLLATOR_OPTIONS: Readonly<Intl.CollatorOptions> = {
  sensitivity: 'base',
  numeric: true,
};
const ENGLISH_COLLATOR = new Intl.Collator(NAME_SORT_COLLATOR_LOCALE, NAME_SORT_COLLATOR_OPTIONS);

/**
 * Formats a changePercent value for display in the performance column.
 * Positive: green with explicit '+', e.g. '+12,45 %'
 * Zero: neutral '0,00 %'
 * Negative: red, e.g. '-3,20 %'
 * null/undefined: unavailable
 */
export function formatPerformance(changePercent: number | null | undefined): PerformanceDisplayValue {
  if (changePercent == null) return { kind: 'unavailable' };

  const abs = Math.abs(changePercent).toFixed(2).replace('.', ',');
  let formatted: string;
  let color: string;

  if (changePercent > 0 && abs !== '0,00') {
    formatted = `+${abs}\u00a0%`;
    color = COLOR_POSITIVE;
  } else if (changePercent < 0 && abs !== '0,00') {
    formatted = `-${abs}\u00a0%`;
    color = COLOR_NEGATIVE;
  } else {
    formatted = `0,00\u00a0%`;
    color = 'inherit';
  }

  return { kind: 'available', changePercent, formatted, color };
}

/**
 * Sorts constituents by performance descending (largest gain first), nulls last.
 * Deterministic tie-breaker: stockId ascending.
 */
export function sortConstituentsByPerformance(
  constituents: readonly IndexConstituentDto[],
  performanceMap: PerformanceMap,
): IndexConstituentDto[] {
  return [...constituents].sort((a, b) => {
    const pa = performanceMap.get(a.stockId) ?? null;
    const pb = performanceMap.get(b.stockId) ?? null;
    const aHas = pa != null;
    const bHas = pb != null;

    if (aHas && bHas) {
      if (pa !== pb) return pb - pa; // descending
    } else if (aHas) {
      return -1;
    } else if (bHas) {
      return 1;
    }

    return a.stockId - b.stockId;
  });
}

/**
 * Sorts constituents alphabetically by displayed company name using deterministic
 * English/Latin collation (case-insensitive, numeric-aware).
 * Tie-breakers: ticker (same collator), then stockId ascending.
 */
export function sortConstituentsByName(
  constituents: readonly IndexConstituentDto[],
): IndexConstituentDto[] {
  return [...constituents].sort((a, b) => {
    const byName = ENGLISH_COLLATOR.compare(a.name, b.name);
    if (byName !== 0) return byName;

    const byTicker = ENGLISH_COLLATOR.compare(a.ticker, b.ticker);
    if (byTicker !== 0) return byTicker;

    return a.stockId - b.stockId;
  });
}
