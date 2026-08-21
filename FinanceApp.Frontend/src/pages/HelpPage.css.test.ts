import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const cssText = readFileSync(join(__dirname, 'HelpPage.css'), 'utf-8');
const sourceText = readFileSync(join(__dirname, 'HelpPage.tsx'), 'utf-8');

describe('HelpPage.css readability contract', () => {
  it('keeps article body text readable and scoped to help page', () => {
    expect(cssText).toMatch(/\.help-page__section-body\s*\{[^}]*font-size:\s*18px;[^}]*line-height:\s*1\.6;/);
    expect(cssText).toMatch(/\.help-page\s+\.help-page__section-body\s+\.ant-typography\s*\{/);
  });

  it('uses readable navigation and excerpt typography', () => {
    expect(cssText).toMatch(/\.help-page__article-list\s*\{[^}]*font-size:\s*18px;/);
    expect(cssText).toMatch(/\.help-page__excerpt\s*\{[^}]*font-size:\s*18px;[^}]*line-height:\s*1\.5;/);
    expect(sourceText).toContain('Центр справки FinanceApp');
    expect(sourceText).toContain('<BookOutlined />');
  });

  it('keeps lists, faq and tables at readable minimum sizes', () => {
    expect(cssText).toMatch(/\.help-page\s+\.help-page__section-body\s+li\s*\{[^}]*margin-bottom:\s*6px;/);
    expect(cssText).toMatch(/\.help-page\s+\.help-page__qa-question[\s\S]*font-size:\s*18px;/);
    expect(cssText).toMatch(/\.help-page__table th,[\s\S]*\.help-page__table td\s*\{[^}]*font-size:\s*18px;[^}]*line-height:\s*1\.5;/);
  });

  it('applies shared directories typography wrapper for ant controls, search and alerts', () => {
    expect(sourceText).toContain('DIRECTORIES_TYPOGRAPHY_CLASS');
    expect(sourceText).toContain('Input.Search');
    expect(sourceText).toContain('<Card key={category.slug} size="small" title={category.title}>');
    expect(sourceText).toContain('<Button type="link" onClick={() => onSearchChange(\'\')}');
    expect(sourceText).toContain('data-responsive="stack-lg"');
  });

  it('keeps help heading hierarchy with explicit title levels', () => {
    expect(sourceText).toContain('<Title level={2}>{selectedArticle.title}</Title>');
    expect(sourceText).toContain('<Title level={3}>Центр справки FinanceApp</Title>');
    expect(sourceText).toContain('<Title level={3} id={`heading-${section.slug}`}>{section.title}</Title>');
  });

  it('preserves wide table usability with horizontal scrolling', () => {
    expect(cssText).toMatch(/\.help-page__table-wrap\s*\{[^}]*overflow-x:\s*auto;/);
    expect(cssText).toMatch(/\.help-page__table\s*\{[^}]*width:\s*100%;/);
  });
});
