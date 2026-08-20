import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const cssText = readFileSync(join(__dirname, 'HelpPage.css'), 'utf-8');

describe('HelpPage typography CSS contract', () => {
  it('keeps readable body typography scoped to .help-page', () => {
    expect(cssText).toMatch(/\.help-page\s*\{[^}]*--help-body-font-size:\s*16px/);
    expect(cssText).toMatch(/\.help-page\s*\{[^}]*--help-body-line-height:\s*1\.6/);
    expect(cssText).toMatch(/\.help-page__body-text,\s*\.help-page__qa-question\s*\{[^}]*font-size:\s*var\(--help-body-font-size\)/);
    expect(cssText).toMatch(/\.help-page__body-text,\s*\.help-page__qa-question\s*\{[^}]*line-height:\s*var\(--help-body-line-height\)/);
  });

  it('keeps navigation and table text at comfortable minimum sizes', () => {
    expect(cssText).toMatch(/\.help-page\s*\{[^}]*--help-nav-font-size:\s*15px/);
    expect(cssText).toMatch(/\.help-page\s*\{[^}]*--help-table-font-size:\s*15px/);
    expect(cssText).toMatch(/\.help-page__article-list\s*\{[^}]*font-size:\s*var\(--help-nav-font-size\)/);
    expect(cssText).toMatch(/\.help-page__table th,\s*\.help-page__table td\s*\{[^}]*font-size:\s*var\(--help-table-font-size\)/);
  });

  it('keeps search excerpts readable and retains horizontal table scrolling', () => {
    expect(cssText).toMatch(/\.help-page__excerpt\s*\{[^}]*font-size:\s*14px/);
    expect(cssText).toMatch(/\.help-page__excerpt\s*\{[^}]*line-height:\s*1\.5/);
    expect(cssText).toMatch(/\.help-page__table-wrap\s*\{[^}]*overflow-x:\s*auto/);
  });

  it('keeps mobile body typography at least 16px and scoped in media query', () => {
    expect(cssText).toMatch(/@media\s*\(max-width:\s*992px\)\s*\{[\s\S]*\.help-page\s*\{[\s\S]*--help-body-font-size:\s*16px/);
    expect(cssText).toMatch(/@media\s*\(max-width:\s*992px\)\s*\{[\s\S]*\.help-page__sidebar\s*\{[\s\S]*position:\s*static/);
  });
});
