import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const readPageFile = (name: string) => readFileSync(join(__dirname, name), 'utf-8');
const readRootCss = () => readFileSync(join(__dirname, '..', 'index.css'), 'utf-8');

describe('Directories typography scope contracts', () => {
  it('keeps scoped 18px rules for generic text, cards, controls, tables and overlays', () => {
    const css = readPageFile('directoriesTypography.css');

    expect(css).toContain('.directories-typography');
    expect(css).toContain('--directories-min-font-size: 18px;');
    expect(css).toContain('.directories-typography .ant-typography:not(h1):not(h2):not(h3):not(h4):not(h5):not(h6)');
    expect(css).toContain('.directories-typography .ant-card-head-title');
    expect(css).toContain('.directories-typography .ant-card-body');
    expect(css).toContain('.directories-typography .ant-btn');
    expect(css).toContain('.directories-typography .ant-table-thead > tr > th');
    expect(css).toContain('.directories-typography .ant-table-tbody > tr > td');
    expect(css).toContain('.directories-typography .ant-tag');
    expect(css).toContain('.directories-typography .ant-form-item-explain-error');
    expect(css).toContain('.directories-typography .ant-input::placeholder');
    expect(css).toContain('.directories-overlay-typography .ant-modal-title');
    expect(css).toContain('.directories-overlay-typography .ant-popconfirm-title');
    expect(css).toContain('.directories-overlay-typography .ant-tooltip-inner');
    expect(css).toContain('.directories-overlay-typography .ant-select-item');
    expect(css).toContain('.directories-overlay-typography .ant-input::placeholder');
    expect(css).toContain('font-size: 18px;');
  });

  it('does not raise global body, #root or app-wide ant baseline to 18px', () => {
    const css = readPageFile('directoriesTypography.css');
    const indexCss = readRootCss();

    expect(css).not.toMatch(/(^|\n)\s*body\s*\{/);
    expect(css).not.toMatch(/(^|\n)\s*#root\s*\{/);
    expect(indexCss).toMatch(/body\s*\{[^}]*font-size:\s*16px;/);
    expect(indexCss).toMatch(/#root,[\s\S]*\.ant-alert-description\s*\{[^}]*font-size:\s*16px;/);
  });

  it('wires sectors overlays and wrappers so modals/tooltips/popconfirms/selects use scoped 18px styles', () => {
    const sectorsSource = readPageFile('SectorsPage.tsx');

    expect(sectorsSource).toContain('className={DIRECTORIES_TYPOGRAPHY_CLASS}');
    expect(sectorsSource).toContain('overlayClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}');
    expect(sectorsSource).toContain('rootClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}');
    expect(sectorsSource).toContain('popupClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}');
    expect(sectorsSource).toContain('<Table<SectorDto>');
    expect(sectorsSource).toContain('<Table<IndustryDto>');
  });

  it('does not define visible text below 18px in scoped wrappers except button icons', () => {
    const css = readPageFile('directoriesTypography.css');
    const withoutIconRule = css.replace(
      /\.directories-typography \.ant-btn \.anticon,[\s\S]*?font-size:\s*16px;[\s\S]*?\}/,
      '',
    );

    expect(withoutIconRule).not.toMatch(/font-size:\s*(?:[0-9]|1[0-7])px/);
  });
});
