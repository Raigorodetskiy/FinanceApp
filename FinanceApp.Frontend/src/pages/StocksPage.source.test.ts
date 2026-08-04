import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const stocksPageSource = readFileSync(resolve(__dirname, './StocksPage.tsx'), 'utf8');

describe('StocksPage source layout', () => {
  it('keeps change columns as separate top-level leaf columns with no nested parent children structure', () => {
    expect(stocksPageSource).toContain("title: 'Изменение (€)'");
    expect(stocksPageSource).toContain("key: 'changeEur'");
    expect(stocksPageSource).toContain("title: '(%)'");
    expect(stocksPageSource).toContain("key: 'changePct'");
    expect(stocksPageSource).not.toContain("title: 'Изменение',\n      key: 'change',\n      children:");
  });

  it('places API price directly before actions', () => {
    const apiIndex = stocksPageSource.indexOf("title: 'Цена API'");
    const actionsIndex = stocksPageSource.indexOf("title: 'Действия'");
    expect(apiIndex).toBeGreaterThan(-1);
    expect(actionsIndex).toBeGreaterThan(-1);
    expect(apiIndex).toBeLessThan(actionsIndex);
  });

  it('renders short local timestamp format and updated chart row span', () => {
    expect(stocksPageSource).toContain("PRICE_TIME_FORMAT = 'DD.MM.YY HH:mm'");
    expect(stocksPageSource).toContain('export const STOCKS_TABLE_TOTAL_COLS = 8;');
    expect(stocksPageSource).toContain('props: { colSpan: TOTAL_COLS }');
  });

  it('removes raw API quote text and absent dash from the actions cell', () => {
    const actionsBlock = stocksPageSource.slice(
      stocksPageSource.indexOf("title: 'Действия'"),
      stocksPageSource.indexOf('  ];')
    );
    expect(actionsBlock).not.toContain('rawQuoteText');
    expect(actionsBlock).not.toContain("?? '—'");
  });
});
