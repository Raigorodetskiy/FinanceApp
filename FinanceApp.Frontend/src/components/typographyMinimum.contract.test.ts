import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const readFrontendFile = (relativePathFromSrc: string) =>
  readFileSync(join(__dirname, '..', relativePathFromSrc), 'utf8');

describe('frontend minimum typography contracts', () => {
  it('keeps help/sidebar/technical-analysis CSS at 16px+ for user-visible text', () => {
    const sidebarCss = readFrontendFile('components/AppSidebar.css');
    const helpCss = readFrontendFile('pages/HelpPage.css');
    const taCss = readFrontendFile('components/StockTechnicalAnalysisPanel.css');

    expect(sidebarCss).not.toMatch(/font-size:\s*(?:[0-9]|1[0-5])px/);
    expect(helpCss).not.toMatch(/font-size:\s*(?:[0-9]|1[0-5])px/);
    expect(taCss).not.toMatch(/font-size:\s*(?:[0-9]|1[0-5])px/);
  });

  it('keeps stock/index chart text and tooltip typography at 16px+', () => {
    const stockChartSource = readFrontendFile('components/StockPriceChart.tsx');
    const indexChartSource = readFrontendFile('components/MarketIndexPriceChart.tsx');

    expect(stockChartSource).not.toMatch(/fontSize:\s*(?:[0-9]|1[0-5])\b/);
    expect(indexChartSource).not.toMatch(/fontSize:\s*(?:[0-9]|1[0-5])\b/);
  });

  it('allows sub-16 icon glyph sizing only for caret icons in expandable rows', () => {
    const stocksPageSource = readFrontendFile('pages/StocksPage.tsx');
    const indexConstituentsSource = readFrontendFile('components/IndexConstituentsPanel.tsx');
    const portfolioSource = readFrontendFile('pages/PortfolioDetailPage.tsx');

    const strippedStocks = stocksPageSource.replace(/fontSize:\s*10/g, '');
    const strippedIndexConstituents = indexConstituentsSource.replace(/fontSize:\s*10/g, '');
    const strippedPortfolio = portfolioSource.replace(/fontSize:\s*10/g, '');

    expect(stocksPageSource).toContain('<CaretRightFilled');
    expect(indexConstituentsSource).toContain('<CaretRightFilled');
    expect(portfolioSource).toContain('<CaretRightFilled');

    expect(strippedStocks).not.toMatch(/fontSize:\s*(?:[0-9]|1[0-5])\b/);
    expect(strippedIndexConstituents).not.toMatch(/fontSize:\s*(?:[0-9]|1[0-5])\b/);
    expect(strippedPortfolio).not.toMatch(/fontSize:\s*(?:[0-9]|1[0-5])\b/);
  });

  it('keeps global ant typography baseline at 16px', () => {
    const indexCss = readFrontendFile('index.css');
    expect(indexCss).toMatch(/body\s*\{[^}]*font-size:\s*16px;/);
    expect(indexCss).toContain('.ant-table-thead > tr > th');
    expect(indexCss).toContain('.ant-tag');
    expect(indexCss).toContain('.ant-tooltip-inner');
  });
});
