import { describe, expect, it } from 'vitest';
import { HELP_ARTICLES, HELP_CATEGORIES } from './content';
import { ALL_HELP_ARTICLES, getOrderedHelpArticles, getOrderedHelpCategories, normalizeHelpText, searchHelpArticles, validateHelpContent } from './utils';

const getTechnicalTutorial = () => ALL_HELP_ARTICLES.find((item) => item.slug === 'technical-indicator-formulas');

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
    const serialized = JSON.stringify(HELP_ARTICLES.find((item) => item.slug === 'analytical-signal'));
    for (const term of ['StrongBullish', 'ModeratelyBullish', 'Neutral', 'ModeratelyBearish', 'StrongBearish', '35%', '45%', 'Score — агрегированная оценка', 'Confidence', 'не является персональной инвестиционной рекомендацией']) {
      expect(serialized).toContain(term);
    }
  });

  it('documents every exposed technical metric and exact warm-up requirements', () => {
    const article = getTechnicalTutorial();
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
    const serialized = JSON.stringify(getTechnicalTutorial());
    for (const formula of [
      'k = 2 / (N + 1)',
      'RSI = 100 − 100 / (1 + RS)',
      'MACD line = EMA12 − EMA26',
      'Histogram = MACD line − Signal line',
      'Pпоследняя / P N торговых дней назад − 1',
      'σpopulation(r) × √252',
      'Pпоследняя / MaxCloseокна − 1',
      'TR = max(High − Low, |High − Previous Close|, |Low − Previous Close|)',
      'Score = Σ(ComponentScoreᵢ × NormalizedWeightᵢ)',
    ]) {
      expect(serialized).toContain(formula);
    }
    for (const boundary of ['55–70 включительно', '>40%', '< −35%', '>5%', 'Histogram ≥ 0', 'clamp в −25…+25']) {
      expect(serialized).toContain(boundary);
    }
  });

  it('clarifies price basis, drawdown naming, null semantics and read-only behavior', () => {
    const serialized = JSON.stringify(getTechnicalTutorial());
    for (const phrase of [
      'per-candle fallback',
      'AdjustedClose coverage',
      'не скорректированные High, Low',
      'Поле API/UI называется MaxDrawdown',
      'Это НЕ максимальная историческая просадка',
      'null и показывается как «Недостаточно данных»',
      'не запускает запрос к провайдеру',
    ]) {
      expect(serialized).toContain(phrase);
    }
  });

  it('keeps deep-link target sections for every indicator and aggregation rules', () => {
    const slugs = new Set(getTechnicalTutorial()?.sections.map((section) => section.slug));
    for (const slug of [
      'indicator-methodology', 'indicator-sma-ema', 'indicator-rsi14', 'indicator-macd',
      'indicator-returns', 'indicator-volatility-drawdown', 'indicator-atr14',
      'indicator-scoring', 'indicator-missing-confidence',
    ]) {
      expect(slugs.has(slug)).toBe(true);
    }
  });

  it('includes a complete score example, renormalization, Confidence and disclaimer', () => {
    const serialized = JSON.stringify(getTechnicalTutorial());
    for (const phrase of [
      '83×0,35 + 80×0,35 + 58×0,20 + 50×0,10 = 73,65',
      'Новый Score = (83×0,35 + 58×0,20 + 50×0,10) / 0,65 = 70,23',
      'покрытия истории (45%)',
      'свежести (20%)',
      'AdjustedClose coverage (15%)',
      'доступности компонентов (20%)',
      'Score — детерминированная эвристика, а не справедливая стоимость, прогноз, гарантия результата или персональная инвестиционная рекомендация.',
    ]) {
      expect(serialized).toContain(phrase);
    }
  });

  it('documents AdjustedClose/Close fallback and ATR unadjusted OHLC semantics', () => {
    const serialized = JSON.stringify(HELP_ARTICLES.find((item) => item.slug === 'technical-indicators'));
    expect(serialized).toContain('AdjustedClose');
    expect(serialized).toContain('fallback на Close');
    expect(serialized).toContain('ATR14');
    expect(serialized).toContain('не-скорректированному OHLC');
  });

  it('keeps explicit roles for short overview vs full technical tutorial', () => {
    const overview = HELP_ARTICLES.find((item) => item.slug === 'technical-indicators');
    const tutorial = getTechnicalTutorial();
    expect(overview?.title).toContain('краткий обзор');
    expect(JSON.stringify(overview)).toContain('Полный учебный разбор формул и примеров');
    expect(tutorial?.title).toBe('Технические показатели: формулы, примеры и ограничения');
    expect(JSON.stringify(tutorial)).toContain('Краткий обзор технических показателей');
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

  it('documents catalog global period sorting and 24h constituent fallback semantics', () => {
    const stocksAndIndices = HELP_ARTICLES.find((item) => item.slug === 'stocks-and-indices');
    const serialized = JSON.stringify(stocksAndIndices);
    for (const phrase of [
      'единый глобальный список',
      'не влияют на порядок при периодной сортировке',
      'всегда уходят в конец',
      '24ч',
      'текущий snapshot/изменение к предыдущему закрытию',
      'выходных и закрытом рынке',
    ]) {
      expect(serialized).toContain(phrase);
    }
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
  });

  it('finds the tutorial by beginner Russian, abbreviations and English names', () => {
    for (const query of [
      'что такое скользящая средняя', 'экспоненциальная средняя', 'формула RSI',
      'relative strength index', 'MACD Signal Histogram', 'логарифмическая доходность',
      'True Range', 'MaxDrawdown', 'AdjustedClose fallback', 'Недостаточно данных',
    ]) {
      expect(searchHelpArticles(query).some((result) => result.articleSlug === 'technical-indicator-formulas')).toBe(true);
    }
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
