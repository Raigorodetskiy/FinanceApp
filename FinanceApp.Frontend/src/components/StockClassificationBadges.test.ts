import { describe, expect, it } from 'vitest';
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(__dirname, 'StockClassificationBadges.tsx'), 'utf8');

describe('StockClassificationBadges source contract', () => {
  it('uses visible combined classification text with accessibility attrs', () => {
    expect(source).toContain('`${sector} · ${industry}`');
    expect(source).toContain('title={classificationText}');
    expect(source).toContain('aria-label={`Классификация: ${classificationText}`}');
  });

  it('handles missing values quietly', () => {
    expect(source).toContain('if (!classificationText)');
    expect(source).toContain('return null;');
  });
});
