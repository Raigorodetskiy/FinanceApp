import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const pageSource = readFileSync(join(__dirname, 'StocksPage.tsx'), 'utf-8');

describe('tracked-stocks group heading labels', () => {
  it('uses exact "Портфель" heading for portfolio group', () => {
    expect(pageSource).toContain("renderGroup('Портфель',");
  });

  it('uses exact "Цены на франкфуртской бирже" heading for FRA group', () => {
    expect(pageSource).toContain("renderGroup('Цены на франкфуртской бирже',");
  });

  it('uses exact "Цены на нью-йоркской бирже" heading for NYSE group', () => {
    expect(pageSource).toContain("renderGroup('Цены на нью-йоркской бирже',");
  });

  it('does not use old FRA or NYSE short heading labels', () => {
    expect(pageSource).not.toContain("renderGroup('FRA',");
    expect(pageSource).not.toContain("renderGroup('NYSE',");
  });

  it('renders InfoCircleOutlined icon in group header', () => {
    expect(pageSource).toContain('InfoCircleOutlined');
  });
});
