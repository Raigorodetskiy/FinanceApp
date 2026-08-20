export type HelpCalloutType = 'info' | 'important' | 'warning' | 'example';

export type HelpBlock =
  | { type: 'paragraph'; text: string }
  | { type: 'list'; items: string[]; ordered?: boolean }
  | { type: 'table'; columns: string[]; rows: string[][] }
  | { type: 'callout'; calloutType: HelpCalloutType; title?: string; body: string[] }
  | { type: 'qa'; question: string; answer: string };

export interface HelpSection {
  slug: string;
  title: string;
  keywords?: string[];
  blocks: HelpBlock[];
}

export interface HelpRelatedLink {
  articleSlug: string;
  sectionSlug?: string;
  label: string;
}

export interface HelpArticle {
  slug: string;
  categorySlug: string;
  title: string;
  summary: string;
  keywords: string[];
  order: number;
  sections: HelpSection[];
  related?: HelpRelatedLink[];
}

export interface HelpCategory {
  slug: string;
  title: string;
  description: string;
  order: number;
}

export interface HelpSearchResult {
  articleSlug: string;
  sectionSlug?: string;
  score: number;
  matchedField: 'title' | 'summary' | 'keyword' | 'heading' | 'body';
  excerpt: string;
}

export interface HelpContentValidationResult {
  duplicateCategorySlugs: string[];
  duplicateArticleSlugs: string[];
  duplicateSectionSlugs: string[];
  unresolvedLinks: string[];
}
