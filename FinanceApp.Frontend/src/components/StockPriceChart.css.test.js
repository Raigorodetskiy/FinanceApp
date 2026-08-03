import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const cssText = readFileSync(join(__dirname, 'StockPriceChart.css'), 'utf-8');

describe('StockPriceChart.css – segmented underline selectors', () => {
  it('applies the transparent bottom border to .ant-segmented-item (not to the label)', () => {
    expect(cssText).toMatch(
      /\.stock-price-chart-segmented\s+\.ant-segmented-item\s*\{[^}]*border-bottom:\s*3px\s+solid\s+transparent/,
    );
  });

  it('applies the blue border-bottom-color to .ant-segmented-item-selected (not to the label)', () => {
    expect(cssText).toMatch(
      /\.stock-price-chart-segmented\s+\.ant-segmented-item-selected\s*\{[^}]*border-bottom-color:\s*#1677ff/,
    );
  });

  it('does not apply any border to .ant-segmented-item-label', () => {
    // The obsolete label-level border rules must be absent.
    expect(cssText).not.toMatch(/\.ant-segmented-item-label[^{]*\{[^}]*border-bottom/);
  });

  it('sets box-sizing: border-box on .ant-segmented-item to avoid layout shift', () => {
    expect(cssText).toMatch(
      /\.stock-price-chart-segmented\s+\.ant-segmented-item\s*\{[^}]*box-sizing:\s*border-box/,
    );
  });
});
