import { HELP_ARTICLES, HELP_CATEGORIES } from './content';
import { FUNDAMENTAL_SCORING_METHODOLOGY_ARTICLE } from './fundamentalScoringMethodology';
import { TECHNICAL_INDICATOR_FORMULAS_ARTICLE } from './technicalIndicatorFormulas';
import type { HelpArticle, HelpBlock, HelpCategory, HelpContentValidationResult, HelpRelatedLink, HelpSearchResult } from './models';

export const ALL_HELP_ARTICLES: HelpArticle[] = [
  ...HELP_ARTICLES,
  FUNDAMENTAL_SCORING_METHODOLOGY_ARTICLE,
  TECHNICAL_INDICATOR_FORMULAS_ARTICLE,
];

export const normalizeHelpText = (value: string): string => value
  .normalize('NFKC')
  .toLocaleLowerCase('ru-RU')
  .replace(/\s+/g, ' ')
  .trim();

const collectBlockText = (block: HelpBlock): string => {
  switch (block.type) {
    case 'paragraph':
      return block.text;
    case 'list':
      return block.items.join(' ');
    case 'table':
      return `${block.columns.join(' ')} ${block.rows.map((row) => row.join(' ')).join(' ')}`;
    case 'callout':
      return `${block.title ?? ''} ${block.body.join(' ')}`;
    case 'qa':
      return `${block.question} ${block.answer}`;
    default:
      return '';
  }
};

export const getOrderedHelpCategories = (): HelpCategory[] => [...HELP_CATEGORIES]
  .sort((a, b) => a.order - b.order || a.title.localeCompare(b.title, 'ru'));

export const getOrderedHelpArticles = (): HelpArticle[] => [...ALL_HELP_ARTICLES]
  .sort((a, b) => a.order - b.order || a.title.localeCompare(b.title, 'ru'));

export const getHelpArticleBySlug = (slug: string | undefined): HelpArticle | null => {
  if (!slug) return null;
  return ALL_HELP_ARTICLES.find((article) => article.slug === slug) ?? null;
};

export const resolveHelpRelatedLink = (link: HelpRelatedLink): { article: HelpArticle; sectionExists: boolean } | null => {
  const article = getHelpArticleBySlug(link.articleSlug);
  if (!article) return null;
  if (!link.sectionSlug) {
    return { article, sectionExists: true };
  }
  return {
    article,
    sectionExists: article.sections.some((section) => section.slug === link.sectionSlug),
  };
};

export const validateHelpContent = (): HelpContentValidationResult => {
  const categorySeen = new Set<string>();
  const articleSeen = new Set<string>();
  const sectionSeen = new Set<string>();

  const duplicateCategorySlugs: string[] = [];
  const duplicateArticleSlugs: string[] = [];
  const duplicateSectionSlugs: string[] = [];
  const unresolvedLinks: string[] = [];

  for (const category of HELP_CATEGORIES) {
    if (categorySeen.has(category.slug)) duplicateCategorySlugs.push(category.slug);
    categorySeen.add(category.slug);
  }

  for (const article of ALL_HELP_ARTICLES) {
    if (articleSeen.has(article.slug)) duplicateArticleSlugs.push(article.slug);
    articleSeen.add(article.slug);

    for (const section of article.sections) {
      if (sectionSeen.has(section.slug)) duplicateSectionSlugs.push(section.slug);
      sectionSeen.add(section.slug);
    }

    for (const link of article.related ?? []) {
      const resolved = resolveHelpRelatedLink(link);
      if (!resolved || !resolved.sectionExists) {
        unresolvedLinks.push(`${article.slug} -> ${link.articleSlug}${link.sectionSlug ? `#${link.sectionSlug}` : ''}`);
      }
    }
  }

  return {
    duplicateCategorySlugs,
    duplicateArticleSlugs,
    duplicateSectionSlugs,
    unresolvedLinks,
  };
};

const fieldIncludes = (field: string, query: string): boolean => normalizeHelpText(field).includes(query);

const firstExcerpt = (article: HelpArticle, query: string): { excerpt: string; sectionSlug?: string } => {
  for (const section of article.sections) {
    if (fieldIncludes(section.title, query)) {
      return { excerpt: section.title, sectionSlug: section.slug };
    }

    for (const block of section.blocks) {
      const text = collectBlockText(block);
      if (fieldIncludes(text, query)) {
        return { excerpt: text.slice(0, 220), sectionSlug: section.slug };
      }
    }
  }

  return { excerpt: article.summary };
};

export const searchHelpArticles = (queryValue: string): HelpSearchResult[] => {
  const query = normalizeHelpText(queryValue);
  if (!query) return [];

  const scored: HelpSearchResult[] = [];

  for (const article of getOrderedHelpArticles()) {
    const title = normalizeHelpText(article.title);
    const summary = normalizeHelpText(article.summary);
    const keywords = article.keywords.map(normalizeHelpText);
    const headings = article.sections.map((section) => normalizeHelpText(section.title));
    const body = normalizeHelpText(article.sections.map((section) => section.blocks.map(collectBlockText).join(' ')).join(' '));

    let score = -1;
    let matchedField: HelpSearchResult['matchedField'] = 'body';

    if (title === query) {
      score = 1000;
      matchedField = 'title';
    } else if (title.startsWith(query)) {
      score = 900;
      matchedField = 'title';
    } else if (title.includes(query)) {
      score = 800;
      matchedField = 'title';
    } else if (keywords.some((keyword) => keyword === query)) {
      score = 700;
      matchedField = 'keyword';
    } else if (keywords.some((keyword) => keyword.includes(query))) {
      score = 650;
      matchedField = 'keyword';
    } else if (headings.some((heading) => heading.includes(query))) {
      score = 600;
      matchedField = 'heading';
    } else if (summary.includes(query)) {
      score = 500;
      matchedField = 'summary';
    } else if (body.includes(query)) {
      score = 400;
      matchedField = 'body';
    }

    if (score < 0) continue;

    const excerptMeta = firstExcerpt(article, query);
    scored.push({
      articleSlug: article.slug,
      sectionSlug: excerptMeta.sectionSlug,
      score,
      matchedField,
      excerpt: excerptMeta.excerpt,
    });
  }

  return scored.sort((a, b) => b.score - a.score || a.articleSlug.localeCompare(b.articleSlug, 'ru'));
};

export const buildHelpArticleUrl = (articleSlug: string, sectionSlug?: string): string =>
  `/help/${articleSlug}${sectionSlug ? `#${sectionSlug}` : ''}`;
