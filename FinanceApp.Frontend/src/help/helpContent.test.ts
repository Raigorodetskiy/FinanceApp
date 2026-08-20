import { describe, expect, it } from 'vitest';
import { HELP_ARTICLES, HELP_CATEGORIES } from './content';
import { getOrderedHelpArticles, getOrderedHelpCategories, normalizeHelpText, searchHelpArticles, validateHelpContent } from './utils';

describe('help content contracts', () => {
  const getTechnicalIndicatorsArticle = () => {
    const article = HELP_ARTICLES.find((item) => item.slug === 'technical-indicators');
    expect(article).toBeDefined();
    return article!;
  };

  const getSection = (sectionSlug: string) => {
    const article = getTechnicalIndicatorsArticle();
    const section = article.sections.find((item) => item.slug === sectionSlug);
    expect(section).toBeDefined();
    return section!;
  };

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

  it('documents backend calculation source, persisted history, and read-only behavior', () => {
    const serialized = JSON.stringify(getSection('indicators-calculation-source'));
    expect(serialized).toContain('рассчитываются backend-кодом');
    expect(serialized).toContain('не приходят готовыми');
    expect(serialized).toContain('сохранённым дневным свечам');
    expect(serialized).toContain('не вызывает провайдера');
    expect(serialized).toContain('не запускает refresh');
  });

  it('documents AdjustedClose/Close per-candle fallback and stale caveat', () => {
    const source = JSON.stringify(getSection('indicators-calculation-source'));
    const article = JSON.stringify(getSection('indicators-price-basis-and-history'));
    expect(source).toContain('валидный AdjustedClose в приоритете');
    expect(source).toContain('fallback на Close этой же свечи');
    expect(source).toContain('может быть stale');
    expect(article).toContain('ATR14');
    expect(article).toContain('не-скорректированному OHLC');
  });

  it('documents RSI14 minimum history, Wilder smoothing, formulas, edge cases, and score thresholds', () => {
    const serialized = JSON.stringify(getSection('indicators-rsi14-calculation'));
    expect(serialized).toContain('минимум 15 цен закрытия');
    expect(serialized).toContain('не-положительная цена (<= 0)');
    expect(serialized).toContain('арифметическое среднее первых 14 изменений');
    expect(serialized).toContain('(prevAvg * 13 + currentGainOrLoss) / 14');
    expect(serialized).toContain('RS = averageGain / averageLoss');
    expect(serialized).toContain('RSI = 100 - 100 / (1 + RS)');
    expect(serialized).toContain('RSI = 100');
    expect(serialized).toContain('55–70');
    expect(serialized).toContain('>70');
    expect(serialized).toContain('45–<55');
    expect(serialized).toContain('30–<45');
    expect(serialized).toContain('<30');
    expect(serialized).toContain('не автоматическая команда Buy/Sell');
    expect(serialized).toContain('RSI ≈ 50');
  });

  it('documents MACD 12/26/9 formulas, seeding/alignment, 26/34 behavior, invalid input, and momentum adjustments', () => {
    const serialized = JSON.stringify(getSection('indicators-macd-calculation'));
    expect(serialized).toContain('MACD = EMA12 - EMA26');
    expect(serialized).toContain('2/(12+1)');
    expect(serialized).toContain('2/(26+1)');
    expect(serialized).toContain('2/(9+1)');
    expect(serialized).toContain('SMA первых 12 closes');
    expect(serialized).toContain('SMA первых 26 closes');
    expect(serialized).toContain('индекс 25');
    expect(serialized).toContain('26 наблюдений');
    expect(serialized).toContain('34 закрытия');
    expect(serialized).toContain('Histogram = MACD line - Signal line');
    expect(serialized).toContain('26–33');
    expect(serialized).toContain('не-положительная цена (<= 0)');
    expect(serialized).toContain('Histogram >= 0');
    expect(serialized).toContain('Histogram < 0');
    expect(serialized).toContain('EMA12 > EMA26');
  });

  it('exposes stable RSI/MACD deep-link slugs and searchable keywords', () => {
    const article = getTechnicalIndicatorsArticle();
    const slugs = article.sections.map((section) => section.slug);
    expect(slugs).toContain('indicators-rsi14-calculation');
    expect(slugs).toContain('indicators-macd-calculation');

    expect(article.keywords).toEqual(expect.arrayContaining([
      'RSI',
      'RSI14',
      'MACD',
      'сигнальная линия',
      'гистограмма',
      'формула',
      'рассчитывается программой',
      '26 свечей',
      '34 свечи',
    ]));
  });

  it('covers interpretation and limitations disclaimer topics for RSI/MACD', () => {
    const serialized = JSON.stringify(getSection('indicators-interpretation-limitations'));
    expect(serialized).toContain('Score');
    expect(serialized).toContain('Confidence');
    expect(serialized).toContain('запаздывающие');
    expect(serialized).toContain('могут пересчитаться');
    expect(serialized).toContain('не является персональной инвестиционной рекомендацией');
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
    expect(searchHelpArticles('где находится').some((result) => result.articleSlug === 'faq')).toBe(true);
    expect(searchHelpArticles('ренормализует веса').some((result) => result.articleSlug === 'analytical-signal')).toBe(true);
  });

  it('finds RSI/MACD formula and history queries in Russian and English', () => {
    expect(searchHelpArticles('RSI14').some((result) => result.articleSlug === 'technical-indicators')).toBe(true);
    expect(searchHelpArticles('MACD').some((result) => result.articleSlug === 'technical-indicators')).toBe(true);
    expect(searchHelpArticles('сигнальная линия').some((result) => result.articleSlug === 'technical-indicators')).toBe(true);
    expect(searchHelpArticles('гистограмма').some((result) => result.articleSlug === 'technical-indicators')).toBe(true);
    expect(searchHelpArticles('формула').some((result) => result.articleSlug === 'technical-indicators')).toBe(true);
    expect(searchHelpArticles('26 свечей').some((result) => result.articleSlug === 'technical-indicators')).toBe(true);
    expect(searchHelpArticles('34 свечи').some((result) => result.articleSlug === 'technical-indicators')).toBe(true);
    expect(searchHelpArticles('рассчитывается программой').some((result) => result.articleSlug === 'technical-indicators')).toBe(true);
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
