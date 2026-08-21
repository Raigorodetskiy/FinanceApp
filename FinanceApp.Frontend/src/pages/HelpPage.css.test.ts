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

  it('does not keep fixed article width or 78ch body restriction', () => {
    expect(cssText).not.toMatch(/\.help-page__article\s*\{[^}]*max-width:\s*980px;/);
    expect(cssText).not.toMatch(/max-width:\s*78ch/);
  });

  it('keeps root/sidebar/content/article in shrinkable contained width', () => {
    expect(cssText).toMatch(/\.help-page\s*\{[^}]*min-width:\s*0;[^}]*max-width:\s*100%;/);
    expect(cssText).toMatch(/\.help-page__sidebar\s*\{[^}]*min-width:\s*0;[^}]*max-width:\s*100%;/);
    expect(cssText).toMatch(/\.help-page__content\s*\{[^}]*min-width:\s*0;[^}]*max-width:\s*100%;/);
    expect(cssText).toMatch(/\.help-page__article\s*\{[^}]*min-width:\s*0;[^}]*max-width:\s*100%;/);
    expect(cssText).toMatch(/\.help-page\s+\.ant-card,[\s\S]*\.help-page\s+\.ant-alert,[\s\S]*max-width:\s*100%;/);
  });

  it('wraps headers and action links inside content width', () => {
    expect(cssText).toMatch(/\.help-page__article-header\s*\{[^}]*flex-wrap:\s*wrap;/);
    expect(cssText).toMatch(/\.help-page__section-header\s*\{[^}]*flex-wrap:\s*wrap;/);
    expect(cssText).toMatch(/\.help-page__article-header\s+\.ant-btn,[\s\S]*\.help-page__section-header\s+\.ant-btn\s*\{[^}]*white-space:\s*normal;/);
  });

  it('forces safe wrapping for long links and titles', () => {
    expect(cssText).toMatch(/\.help-page a,[\s\S]*overflow-wrap:\s*anywhere;/);
    expect(cssText).toMatch(/\.help-page a,[\s\S]*word-break:\s*break-word;/);
  });

  it('keeps responsive two-column layout and mobile single-column breakpoint', () => {
    expect(cssText).toMatch(/\.help-page\s*\{[^}]*grid-template-columns:\s*minmax\(240px,\s*clamp\(280px,\s*28vw,\s*360px\)\)\s*minmax\(0,\s*1fr\);/);
    expect(cssText).toMatch(/@media\s*\(max-width:\s*992px\)\s*\{[\s\S]*\.help-page\s*\{[^}]*grid-template-columns:\s*1fr;/);
  });

  it('enforces sidebar category heading minimum typography', () => {
    expect(cssText).toMatch(/\.help-page__sidebar\s+\.ant-card-head-title\s*\{[^}]*font-size:\s*18px\s*!important;[^}]*font-weight:\s*600\s*!important;/);
  });
});
