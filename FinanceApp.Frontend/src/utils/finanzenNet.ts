/** Valid finanzen.net slug: lowercase letters, digits, and hyphens; 1–120 characters; must start with a letter or digit. */
const FINANZEN_NET_SLUG_RE = /^[a-z0-9][a-z0-9-]{0,119}$/;

/**
 * Returns true when `slug` is a non-empty string that matches the allowed
 * finanzen.net slug format (`^[a-z0-9][a-z0-9-]{0,119}$`).
 */
export const isValidFinanzenNetSlug = (slug: unknown): slug is string =>
  typeof slug === 'string' && FINANZEN_NET_SLUG_RE.test(slug);

/**
 * Builds the finanzen.net instrument page URL for the given slug.
 * Returns `null` when the slug is missing or invalid.
 */
export const buildFinanzenNetUrl = (slug: string | null | undefined): string | null => {
  if (!isValidFinanzenNetSlug(slug)) return null;
  return `https://www.finanzen.net/aktien/${encodeURIComponent(slug)}`;
};
