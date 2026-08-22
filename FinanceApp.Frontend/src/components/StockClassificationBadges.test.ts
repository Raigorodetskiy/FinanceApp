import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(__dirname, 'StockClassificationBadges.tsx'), 'utf8');

describe('StockClassificationBadges source contract', () => {
  it('uses compact Russian labels and accessible full-text attrs', () => {
    expect(source).toContain('shortLabel="СЕК"');
    expect(source).toContain('shortLabel="ОТР"');
    expect(source).toContain('title={fullLabel}');
    expect(source).toContain('aria-label={aria}');
  });

  it('handles missing values quietly', () => {
    expect(source).toContain('if (!sector && !industry)');
    expect(source).toContain('return null;');
  });
});
