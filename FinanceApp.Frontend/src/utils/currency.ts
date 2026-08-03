/**
 * Shared currency formatting utilities.
 *
 * All monetary values in table cells must display the currency symbol/code
 * AFTER the numeric value, e.g. "458,92 €" or "123.45 USD".
 */

export type FormatCurrencyOptions = {
  /** When true, prepend "+" for positive values (for P&L / change columns). */
  signed?: boolean;
  /** Number of decimal places. Defaults to 2. */
  decimals?: number;
  /** BCP 47 locale string used for number formatting. Defaults to 'ru-RU'. */
  locale?: string;
};

/**
 * Format a monetary amount with the currency symbol placed after the number.
 *
 * Returns "—" for null / undefined values.
 *
 * Examples (ru-RU locale, EUR):
 *   formatCurrency(458.92, '€')          → "458,92 €"
 *   formatCurrency(12.34, '€', {signed}) → "+12,34 €"
 *   formatCurrency(-12.34, '€')          → "-12,34 €"
 *   formatCurrency(0, '€', {signed})     → "0,00 €"
 *   formatCurrency(null, '€')            → "—"
 */
export function formatCurrency(
  value: number | null | undefined,
  symbol: string,
  options: FormatCurrencyOptions = {},
): string {
  if (value == null || !isFinite(value)) return '—';

  const { signed = false, decimals = 2, locale = 'ru-RU' } = options;

  const absFormatted = Math.abs(value).toLocaleString(locale, {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });

  const sign = value < 0 ? '-' : signed && value > 0 ? '+' : '';
  return `${sign}${absFormatted} ${symbol}`;
}

/**
 * Format a percentage value with an explicit sign for P&L / change columns.
 *
 * Returns "—" for null / undefined values.
 *
 * Examples:
 *   formatPercent(1.23)   → "+1,23 %"
 *   formatPercent(-1.23)  → "-1,23 %"
 *   formatPercent(0)      → "0,00 %"
 */
export function formatPercent(
  value: number | null | undefined,
  options: Omit<FormatCurrencyOptions, 'signed'> = {},
): string {
  if (value == null || !isFinite(value)) return '—';

  const { decimals = 2, locale = 'ru-RU' } = options;

  const absFormatted = Math.abs(value).toLocaleString(locale, {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });

  const sign = value < 0 ? '-' : value > 0 ? '+' : '';
  return `${sign}${absFormatted} %`;
}
