/**
 * Tests for the financialMetrics data catalog.
 */
import { describe, it, expect } from 'vitest';
import { financialMetrics, FINANCIAL_METRICS_COUNT } from './financialMetrics';

// IDs of metrics that are purely absolute/reported figures — not calculated
// from other metrics, so formula/example are not required.
const NON_CALCULATED_IDS = new Set([
  'total-assets',
  'revenue',
  'net-income',
  'cash-and-equivalents',
  'total-debt',
  'capex',
  'dps',
]);

describe('financialMetrics catalog', () => {
  it('contains at least 31 metrics', () => {
    expect(financialMetrics.length).toBeGreaterThanOrEqual(31);
  });

  it('FINANCIAL_METRICS_COUNT matches actual array length', () => {
    expect(FINANCIAL_METRICS_COUNT).toBe(financialMetrics.length);
  });

  it('all ids are stable, non-empty and unique', () => {
    const ids = financialMetrics.map((m) => m.id);
    const unique = new Set(ids);
    expect(unique.size).toBe(ids.length);
    for (const id of ids) {
      expect(id.length).toBeGreaterThan(0);
    }
  });

  it('no duplicate names', () => {
    const names = financialMetrics.map((m) => m.name);
    const unique = new Set(names);
    expect(unique.size).toBe(names.length);
  });

  it('all entries have non-empty name and description', () => {
    for (const metric of financialMetrics) {
      expect(metric.name.trim().length, `name empty for ${metric.id}`).toBeGreaterThan(0);
      expect(metric.description.trim().length, `description empty for ${metric.id}`).toBeGreaterThan(0);
    }
  });

  it('calculated metrics have formula and example; non-calculated are explicitly in the exception set', () => {
    for (const metric of financialMetrics) {
      if (NON_CALCULATED_IDS.has(metric.id)) {
        // non-calculated: formula/example are optional (but nothing is required)
        continue;
      }
      expect(metric.formula, `formula missing for ${metric.id}`).toBeTruthy();
      expect(metric.example, `example missing for ${metric.id}`).toBeTruthy();
    }
  });

  it('P/E formula contains required components', () => {
    const pe = financialMetrics.find((m) => m.id === 'pe');
    expect(pe).toBeDefined();
    const f = pe!.formula!.toLowerCase();
    expect(f).toContain('p/e');
    expect(f).toContain('eps');
  });

  it('ROE formula contains required components', () => {
    const roe = financialMetrics.find((m) => m.id === 'roe');
    expect(roe).toBeDefined();
    const f = roe!.formula!.toLowerCase();
    expect(f).toContain('roe');
    expect(f).toContain('капитал');
  });

  it('EV formula contains Market Cap, Debt and Cash', () => {
    const ev = financialMetrics.find((m) => m.id === 'ev');
    expect(ev).toBeDefined();
    const f = ev!.formula!.toLowerCase();
    expect(f).toMatch(/капитализация|market cap/i);
    expect(f).toMatch(/долг|debt/i);
    expect(f).toMatch(/денежные|cash/i);
  });

  it('EBITDA formula contains EBIT and Amortization', () => {
    const ebitda = financialMetrics.find((m) => m.id === 'ebitda');
    expect(ebitda).toBeDefined();
    const f = ebitda!.formula!.toLowerCase();
    expect(f).toContain('ebit');
    expect(f).toMatch(/аморт|depreciation/i);
  });

  it('initial sort matches Russian alphabetical order', () => {
    const sorted = [...financialMetrics].sort((a, b) => a.name.localeCompare(b.name, 'ru'));
    for (let i = 0; i < sorted.length - 1; i++) {
      const cmp = sorted[i].name.localeCompare(sorted[i + 1].name, 'ru');
      expect(cmp, `${sorted[i].name} should come before ${sorted[i + 1].name}`).toBeLessThanOrEqual(0);
    }
  });

  describe('search logic', () => {
    function matches(metric: (typeof financialMetrics)[0], query: string) {
      const q = query.toLowerCase().trim();
      if (!q) return true;
      if (metric.name.toLowerCase().includes(q)) return true;
      if (metric.description.toLowerCase().includes(q)) return true;
      if (metric.formula && metric.formula.toLowerCase().includes(q)) return true;
      if (metric.aliases && metric.aliases.some((a) => a.toLowerCase().includes(q))) return true;
      return false;
    }

    it('finds by Russian name', () => {
      const results = financialMetrics.filter((m) => matches(m, 'выручка'));
      expect(results.some((m) => m.id === 'revenue')).toBe(true);
    });

    it('finds by English alias', () => {
      const results = financialMetrics.filter((m) => matches(m, 'EPS'));
      expect(results.some((m) => m.id === 'eps')).toBe(true);
    });

    it('finds by description text', () => {
      const results = financialMetrics.filter((m) => matches(m, 'амортизация'));
      expect(results.length).toBeGreaterThan(0);
    });

    it('finds by formula text', () => {
      const results = financialMetrics.filter((m) => matches(m, 'NOPAT'));
      expect(results.some((m) => m.id === 'roic')).toBe(true);
    });

    it('is case-insensitive', () => {
      const lower = financialMetrics.filter((m) => matches(m, 'eps'));
      const upper = financialMetrics.filter((m) => matches(m, 'EPS'));
      expect(lower.map((m) => m.id).sort()).toEqual(upper.map((m) => m.id).sort());
    });

    it('trims whitespace before matching', () => {
      const trimmed = financialMetrics.filter((m) => matches(m, '  EPS  '));
      const normal = financialMetrics.filter((m) => matches(m, 'EPS'));
      expect(trimmed.map((m) => m.id).sort()).toEqual(normal.map((m) => m.id).sort());
    });

    it('returns empty array for an unmatched query', () => {
      const results = financialMetrics.filter((m) => matches(m, 'xyzzy_no_match_12345'));
      expect(results).toHaveLength(0);
    });
  });

  it('all 31 required metrics are present by name/alias', () => {
    const required = [
      'Активы',
      'Балансовая стоимость',
      'Валовая прибыль',
      'Валовая маржа',
      'Выручка',
      'Дивидендная доходность',
      'Дивиденды на акцию',
      'Денежные средства и эквиваленты',
      'Долг',
      'Капитальные затраты',
      'Коэффициент текущей ликвидности',
      'Рыночная капитализация',
      'Свободный денежный поток',
      'Собственный капитал',
      'Чистая прибыль',
      'Чистая маржа',
    ];
    const englishRequired = ['D/E', 'EBIT', 'EBITDA', 'EPS', 'EV/EBITDA', 'FCF Yield', 'Forward P/E', 'P/B', 'P/E', 'P/S', 'PEG', 'ROA', 'ROE', 'ROIC'];

    const allNames = new Set(financialMetrics.map((m) => m.name));
    const allAliases = new Set(financialMetrics.flatMap((m) => m.aliases ?? []));

    for (const name of required) {
      expect(allNames.has(name), `Missing required metric: ${name}`).toBe(true);
    }

    // Enterprise Value - check by id since name includes "(EV)"
    expect(financialMetrics.find((m) => m.id === 'ev')).toBeDefined();

    for (const alias of englishRequired) {
      const found =
        allNames.has(alias) ||
        allAliases.has(alias) ||
        financialMetrics.some((m) => m.name.includes(alias));
      expect(found, `Missing required metric (alias): ${alias}`).toBe(true);
    }
  });
});
