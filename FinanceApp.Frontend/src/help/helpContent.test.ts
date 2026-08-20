import { describe, expect, it } from 'vitest';
import { HELP_ARTICLES, HELP_CATEGORIES } from './content';
import { getOrderedHelpArticles, getOrderedHelpCategories, normalizeHelpText, searchHelpArticles, validateHelpContent } from './utils';

describe('help content contracts', () => {
  it('keeps deterministic ordering for categories and articles', () => {
    const categoryOrders = getOrderedHelpCategories().map((category) => category.order);
    const articleOrders = getOrderedHelpArticles().map((article) => article.order);

    expect(categoryOrders).toEqual([...categoryOrders].sort((a, b) => a - b));
    expect(articleOrders).toEqual([...articleOrders].sort((a, b) => a - b));
  });

  it('ensures category/article/section slugs are globally unique and links resolve', () => {
    const validation = validateHelpContent();
    expect(validation.duplicateCategorySlugs).toEqual([]);
    expect(validation.duplicateArticleSlugs).toEqual([]);
    expect(validation.duplicateSectionSlugs).toEqual([]);
    expect(validation.unresolvedLinks).toEqual([]);
  });

  it('contains all required analytical signal thresholds, weights, and disclaimer topics', () => {
    const article = HELP_ARTICLES.find((item) => item.slug === 'analytical-signal');
    expect(article).toBeDefined();

    const serialized = JSON.stringify(article);
    expect(serialized).toContain('StrongBullish');
    expect(serialized).toContain('ModeratelyBullish');
    expect(serialized).toContain('Neutral');
    expect(serialized).toContain('ModeratelyBearish');
    expect(serialized).toContain('StrongBearish');
    expect(serialized).toContain('35%');
    expect(serialized).toContain('45%');
    expect(serialized).toContain('Score — агрегированная оценка');
    expect(serialized).toContain('Confidence');
    expect(serialized).toContain('не является персональной инвестиционной рекомендацией');
  });

  it('documents complete technical-indicator methodology semantics and formulas', () => {
    const article = HELP_ARTICLES.find((item) => item.slug === 'technical-indicators');
    expect(article).toBeDefined();
    const serialized = JSON.stringify(article);

    expect(article?.sections.some((section) => section.slug === 'indicators-methodology')).toBe(true);
    expect(serialized).toContain('не вызывает провайдеров');
    expect(serialized).toContain('AsNoTracking');
    expect(serialized).toContain('last value kept');
    expect(serialized).toContain('AdjustedCloseCoverage');

    expect(serialized).toContain('SMA(N) = (Σ Close_i за последние N точек) / N');
    expect(serialized).toContain('k = 2/(period+1)');
    expect(serialized).toContain('RSI=100-100/(1+RS)');
    expect(serialized).toContain('MACD line = EMA12 - EMA26');
    expect(serialized).toContain('Histogram = MACD line - Signal');
    expect(serialized).toContain('Return% = (latest / price_N_days_ago - 1) × 100');
    expect(serialized).toContain('r_t = ln(P_t / P_(t-1))');
    expect(serialized).toContain('Drawdown% = (latest / maxCloseInWindow - 1) × 100');
    expect(serialized).toContain('TR = max(High-Low, |High-PrevClose|, |Low-PrevClose|)');

    expect(serialized).toContain('26 + 9 - 1');
    expect(serialized).toContain('минимум 15 положительных close');
    expect(serialized).toContain('минимум 15 дневных свечей');

    expect(serialized).toContain('RSI 55..70 = +10');
    expect(serialized).toContain('Histogram >= 0 даёт +12');
    expect(serialized).toContain('<=20% даёт +12');
    expect(serialized).toContain('drawdown >= -10% даёт +10');
    expect(serialized).toContain('atrPct <= 2% даёт +6');

    expect(serialized).toContain('AdjustedClose');
    expect(serialized).toContain('fallback на Close');
    expect(serialized).toContain('ATR14');
    expect(serialized).toContain('raw OHLC');
    expect(serialized).toContain('поле DTO/UI называется `MaxDrawdown`');
    expect(serialized).toContain('ренормализуются по доступным компонентам');
    expect(serialized).toContain('не персональная инвестиционная рекомендация');
  });

  it('covers stale/missing fundamentals and data quality caveats', () => {
    const fundamentals = HELP_ARTICLES.find((item) => item.slug === 'fundamentals');
    const quality = HELP_ARTICLES.find((item) => item.slug === 'data-quality-and-freshness');

    expect(JSON.stringify(fundamentals)).toContain('устарев');
    expect(JSON.stringify(fundamentals)).toContain('snapshot');
    expect(JSON.stringify(quality)).toContain('AdjustedClose coverage incomplete');
    expect(JSON.stringify(quality)).toContain('confidence');
  });

  it('contains required top-level categories and FAQ article', () => {
    const categorySlugs = new Set(HELP_CATEGORIES.map((category) => category.slug));
    expect(categorySlugs).toEqual(new Set(['quick-start', 'analytics', 'data-quality', 'stocks-and-indices', 'portfolios', 'faq']));
    expect(HELP_ARTICLES.some((article) => article.slug === 'faq')).toBe(true);
  });
});

describe('help search behavior', () => {
  it('normalizes Russian casing and whitespace', () => {
    expect(normalizeHelpText('  АНАЛИТИЧЕСКИЙ   СИГНАЛ  ')).toBe('аналитический сигнал');
  });

  it('matches title, keywords, headings and body', () => {
    expect(searchHelpArticles('Аналитический сигнал').some((result) => result.articleSlug === 'analytical-signal')).toBe(true);
    expect(searchHelpArticles('AdjustedClose').some((result) => result.articleSlug === 'technical-indicators')).toBe(true);
    expect(searchHelpArticles('Wilder').some((result) => result.articleSlug === 'technical-indicators')).toBe(true);
    expect(searchHelpArticles('True Range').some((result) => result.articleSlug === 'technical-indicators')).toBe(true);
    expect(searchHelpArticles('где находится').some((result) => result.articleSlug === 'faq')).toBe(true);
    expect(searchHelpArticles('ренормализует веса').some((result) => result.articleSlug === 'analytical-signal')).toBe(true);
  });

  it('keeps deterministic ranking with exact title before broad matches', () => {
    const results = searchHelpArticles('Быстрый старт в FinanceApp');
    expect(results[0]?.articleSlug).toBe('quick-start');
    expect(results[0]?.matchedField).toBe('title');
  });

  it('returns empty list for empty/malformed query', () => {
    expect(searchHelpArticles('   ')).toEqual([]);
    expect(searchHelpArticles('\n\t')).toEqual([]);
  });
});
