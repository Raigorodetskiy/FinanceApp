import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(__dirname, 'StockClassificationBadges.tsx'), 'utf8');

describe('StockClassificationBadges source contract', () => {
  it('keeps compact sector-code mapping with accessibility attrs', () => {
    expect(source).toContain("'information technology': 'IT'");
    expect(source).toContain("'communication services': 'COM'");
    expect(source).toContain('title={classificationText}');
    expect(source).toContain('aria-label={`Классификация: ${classificationText}`}');
    expect(source).toContain('{compactCode}');
  });

  it('handles missing values quietly', () => {
    expect(source).toContain('if (!classificationText || !compactCode)');
    expect(source).toContain('return null;');
  });
});
