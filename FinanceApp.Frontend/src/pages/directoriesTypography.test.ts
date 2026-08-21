import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const readPageFile = (name: string) => readFileSync(join(__dirname, name), 'utf-8');
const readRootCss = () => readFileSync(join(__dirname, '..', 'index.css'), 'utf-8');

describe('Directories typography scope contracts', () => {
  it('keeps scoped 18px rules for directory ant controls, table, tags and overlays', () => {
    const css = readPageFile('directoriesTypography.css');

    expect(css).toContain('.directories-typography');
    expect(css).toContain('--directories-min-font-size: 18px;');
    expect(css).toContain('.directories-typography .ant-btn');
    expect(css).toContain('.directories-typography .ant-table-thead > tr > th');
    expect(css).toContain('.directories-typography .ant-table-tbody > tr > td');
    expect(css).toContain('.directories-typography .ant-tag');
    expect(css).toContain('.directories-typography .ant-form-item-explain-error');
    expect(css).toContain('.directories-overlay-typography .ant-modal-title');
    expect(css).toContain('.directories-overlay-typography .ant-popconfirm-title');
    expect(css).toContain('.directories-overlay-typography .ant-tooltip-inner');
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
});
