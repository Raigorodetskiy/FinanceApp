import { describe, expect, it } from 'vitest';
import { HELP_ARTICLES, HELP_CATEGORIES } from './content';
import { ALL_HELP_ARTICLES, getOrderedHelpArticles, getOrderedHelpCategories, normalizeHelpText, searchHelpArticles, validateHelpContent } from './utils';

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

  it('documents every exposed technical metric and exact warm-up requirements', () => {
    const article = ALL_HELP_ARTICLES.find((item) => item.slug === 'technical-indicator-formulas');
    expect(article).toBeDefined();
    const serialized = JSON.stringify(article);

    for (const metric of ['SMA20', 'SMA50', 'SMA200', 'EMA12', 'EMA26', 'RSI14', 'MACD', 'Signal', 'Histogram', 'Volatility20', 'Volatility60', 'MaxDrawdown', 'ATR14']) {
      expect(serialized).toContain(metric);
    }
    for (const requirement of ['15 закрытий', '26 закрытий', '34 закрытия', '22 / 64 / 127 / 253', '21 / 61']) {
      expect(serialized).toContain(requirement);
    }
  });

  it('documents implementation-aligned formulas, scoring thresholds and units', () => {
    const article = ALL_HELP_ARTICLES.find((item) => item.slug === 'technical-indicator-formulas');
    const serialized = JSON.stringify(article);

    expect(serialized).toContain('k = 2 / (N + 1)');
    expect(serialized).toContain('RSI = 100 − 100 / (1 + RS)');
    expect(serialized).toContain('MACD line = EMA12 − EMA26');
    expect(serialized).toContain('Histogram = MACD line − Signal line');
    expect(serialized).toContain('(Pпоследняя / P N торговых дней назад − 1) × 100');
    expect(serialized).toContain('σpopulation(r) × √252');
    expect(serialized).toContain('(Pпоследняя / MaxCloseокна − 1) × 100');
    expect(serialized).toContain('TR = max(High − Low, |High − Previous Close|, |Low − Previous Close|)');
    expect(serialized).toContain('процентных пунктах');
    expect(serialized).toContain('абсолютных единицах цены');
    expect(serialized).toContain('55–70 включительно');
    expect(serialized).toContain('>40%');
    expect(serialized).toContain('< −35%');
    expect(serialized).toContain('>5%');
  });

  it('clarifies price basis, drawdown naming, null semantics and read-only behavior', () => {
    const article = ALL_HELP_ARTICLES.find((item) => item.slug === 'technical-indicator-formulas');
    const serialized = JSON.stringify(article);

    expect(serialized).toContain('per-candle fallback');
    expect(serialized).toContain('AdjustedClose coverage');
    expect(serialized).toContain('не скорректированные High, Low');
    expect(serialized).toContain('Поле API/UI называется MaxDrawdown');
    expect(serialized).toContain('Это НЕ максимальная историческая просадка');
    expect(serialized).toContain('null и показывается как «Недостаточно данных»');
    expect(serialized).toContain('не запускает запрос к провайдеру');
  });

  it('documents AdjustedClose/Close fallback and ATR unadjusted OHLC semantics', () => {
    const article = HELP_ARTICLES.find((item) => item.slug === 'technical-indicators');
    const serialized = JSON.stringify(article);
    expect(serialized).toContain('AdjustedClose');
    expect(serialized).toContain('fallback на Close');
    expect(serialized).toContain('ATR14');
    expect(serialized).toContain('не-скорректированному OHLC');
  });

  it('covers stale/missing fundamentals and data quality caveats', () => {
    const fundamentals = HELP_ARTICLES.find((item) => item.slug === 'fundamentals');
    const quality = HELP_ARTICLES.find((item) => item.slug === 'data-quality-and-freshness');

    expect(JSON.stringify(fundamentals)).toContain('устарев');
    expect(JSON.stringify(fundamentals)).toContain('snapshot');
    expect(JSON.stringify(quality)).toContain('AdjustedClose coverage incomplete');
    expect(JSON.stringify(quality)).toContain('confidence');
  });

  it('documents current fundamental scoring rules, weights, limitations and disclaimer', () => {
    const article = ALL_HELP_ARTICLES.find((item) => item.slug === 'fundamental-scoring-methodology');
    expect(article).toBeDefined();
    const serialized = JSON.stringify(article);

    for (const sectionSlug of [
      'fundamental-methodology-basics',
      'fundamental-methodology-inputs-and-refresh',
      'fundamental-methodology-metric-net-income',
      'fundamental-methodology-metric-fcf',
      'fundamental-methodology-metric-debt-to-ebitda',
      'fundamental-methodology-metric-pe-pb-dy',
      'fundamental-methodology-component-calculation',
      'fundamental-methodology-horizons-and-weights',
      'fundamental-methodology-sector-limitations',
      'fundamental-methodology-history-and-confidence',
      'fundamental-methodology-disclaimer',
    ]) {
      expect(serialized).toContain(sectionSlug);
    }

    for (const expected of [
      'score всегда начинается с 50',
      'clamp) диапазоном 0..100',
      'NetIncomeTtm > 0',
      'FreeCashFlowTtm > 0',
      'DebtToEbitda = TotalDebt / EbitdaTtm',
      'DebtToEbitda < 2',
      'DebtToEbitda > 6',
      'DebtToEbitda > 4 и <= 6',
      '5 <= P/E <= 35',
      'P/E > 60',
      '0.5 <= P/B <= 8',
      'P/B > 15',
      '1 <= DY <= 6',
      'DY > 10',
      'DIVIDEND_YIELD_EXTREME',
      'FUNDAMENTALS_UNUSABLE',
      'FUNDAMENTALS_MISSING',
      'FUNDAMENTALS_STALE',
      'FUNDAMENTAL_HISTORY_INSUFFICIENT',
      'COMPONENTS_MISSING',
      'Финальный score = weightedScore / 0,8',
      '3 месяца',
      '6 месяцев',
      '1 год',
      '2 года',
      '0%',
      '5%',
      '20%',
      '45%',
      'не использует sector/industry',
      'cash-adjusted debt',
      'банков и страховщиков',
      'REIT/real estate',
      'utilities',
      'technology/growth',
      'циклических и временно убыточных',
      'confidence умножается на 0.7',
      'confidence умножается на 0.8',
      'не вычисляются',
      'не intrinsic valuation',
      'не прогноз будущей цены',
      'не персональная инвестиционная рекомендация',
      'не запускает provider refresh',
    ]) {
      expect(serialized).toContain(expected);
    }

    expect(article?.related?.length).toBeGreaterThanOrEqual(3);
    expect(JSON.stringify(article?.related ?? [])).toContain('fundamentals-limitations-and-signal-impact');
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
    expect(searchHelpArticles('формула Уайлдера').some((result) => result.articleSlug === 'technical-indicator-formulas')).toBe(true);
    expect(searchHelpArticles('где находится').some((result) => result.articleSlug === 'faq')).toBe(true);
    expect(searchHelpArticles('ренормализует веса').some((result) => result.articleSlug === 'analytical-signal')).toBe(true);
    expect(searchHelpArticles('фундаментальный анализ').some((result) => result.articleSlug === 'fundamental-scoring-methodology')).toBe(true);
    expect(searchHelpArticles('DebtToEbitda').some((result) => result.articleSlug === 'fundamental-scoring-methodology')).toBe(true);
    expect(searchHelpArticles('FUNDAMENTAL_HISTORY_INSUFFICIENT').some((result) => result.articleSlug === 'fundamental-scoring-methodology')).toBe(true);
    expect(searchHelpArticles('sector/industry').some((result) => result.articleSlug === 'fundamental-scoring-methodology')).toBe(true);
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
