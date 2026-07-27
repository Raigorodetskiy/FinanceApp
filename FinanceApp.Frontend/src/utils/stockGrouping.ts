import type { Stock } from '../types';

export const STOCK_TEXT_LOCALE = 'ru-RU';

/**
 * Compares two stocks alphabetically: primarily by name, with ticker as a tie-breaker.
 */
export const compareStocksAlphabetically = (a: Stock, b: Stock): number => {
  const nameCmp = a.name.localeCompare(b.name, STOCK_TEXT_LOCALE, { sensitivity: 'base' });
  if (nameCmp !== 0) return nameCmp;
  return a.ticker.localeCompare(b.ticker, STOCK_TEXT_LOCALE, { sensitivity: 'base' });
};

export interface StockGroups {
  /** Stocks present in at least one portfolio (deduplicated from exchange groups). */
  portfolioGroup: Stock[];
  /** Frankfurt-exchange stocks not in any portfolio. */
  fraGroup: Stock[];
  /** NYSE stocks (and any other exchange) not in any portfolio. */
  nyseGroup: Stock[];
}

/**
 * Splits a flat stock list into three ordered, deduplicated, alphabetically-sorted groups.
 *
 * Priority:
 *   1. portfolioGroup  – stock is in `portfolioStockIds`
 *   2. fraGroup        – exchange === 'Frankfurt', not in portfolio
 *   3. nyseGroup       – otherwise (NYSE or unknown), not in portfolio
 *
 * Within every group stocks are sorted by name asc, then by ticker asc as a tie-breaker.
 */
export const groupStocks = (stocks: Stock[], portfolioStockIds: ReadonlySet<number>): StockGroups => {
  const portfolioGroup: Stock[] = [];
  const fraGroup: Stock[] = [];
  const nyseGroup: Stock[] = [];

  for (const stock of stocks) {
    if (portfolioStockIds.has(stock.id)) {
      portfolioGroup.push(stock);
    } else if (stock.exchange === 'Frankfurt') {
      fraGroup.push(stock);
    } else {
      nyseGroup.push(stock);
    }
  }

  portfolioGroup.sort(compareStocksAlphabetically);
  fraGroup.sort(compareStocksAlphabetically);
  nyseGroup.sort(compareStocksAlphabetically);

  return { portfolioGroup, fraGroup, nyseGroup };
};
