import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const cssText = readFileSync(join(__dirname, 'HelpPage.css'), 'utf-8');

describe('HelpPage.css typography contracts', () => {
  it('keeps article body text readable at ~16px and 1.6 line-height in scoped selectors', () => {
    expect(cssText).toMatch(
      /\.help-page__content article p,\s*[\s\S]*?\.help-page__content article \.ant-alert-description li\s*\{[^}]*font-size:\s*var\(--help-page-body-font-size\)[^}]*line-height:\s*var\(--help-page-body-line-height\)/,
    );
    expect(cssText).toMatch(/--help-page-body-font-size:\s*16px/);
    expect(cssText).toMatch(/--help-page-body-line-height:\s*1\.6/);
  });

  it('keeps sidebar/article navigation and table text at least 15px, and excerpts at least 14px', () => {
    expect(cssText).toMatch(/--help-page-nav-font-size:\s*15px/);
    expect(cssText).toMatch(/--help-page-nav-line-height:\s*1\.5/);
    expect(cssText).toMatch(/\.help-page__table th,\s*\.help-page__table td\s*\{[^}]*font-size:\s*var\(--help-page-nav-font-size\)/);
    expect(cssText).toMatch(/\.help-page__excerpt\s*\{[^}]*font-size:\s*14px[^}]*line-height:\s*1\.45/);
  });

  it('preserves stacked mobile layout and keeps mobile article text at least 16px', () => {
    expect(cssText).toMatch(/@media \(max-width:\s*992px\)\s*\{[\s\S]*\.help-page\s*\{[\s\S]*grid-template-columns:\s*1fr/);
    expect(cssText).toMatch(/@media \(max-width:\s*768px\)\s*\{[\s\S]*\.help-page__content article p,[\s\S]*font-size:\s*16px[^}]*line-height:\s*1\.6/);
  });

  it('keeps table horizontal scroll and avoids unscoped global typography selectors', () => {
    expect(cssText).toMatch(/\.help-page__table-wrap\s*\{[^}]*overflow-x:\s*auto/);
    expect(cssText).toMatch(/\.help-page__table\s*\{[^}]*min-width:\s*640px/);
    expect(cssText).not.toMatch(/^\s*(?:p|li|ul|ol|th|td)\s*\{/m);
    expect(cssText).not.toMatch(/^\s*\.ant-typography\s*\{/m);
  });
});
