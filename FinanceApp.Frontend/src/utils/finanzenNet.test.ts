import { describe, expect, it } from 'vitest';
import { buildFinanzenNetUrl, isValidFinanzenNetSlug } from './finanzenNet';

describe('isValidFinanzenNetSlug', () => {
  it('accepts a typical lowercase slug with hyphens', () => {
    expect(isValidFinanzenNetSlug('microsoft-aktie')).toBe(true);
  });

  it('accepts a slug with digits', () => {
    expect(isValidFinanzenNetSlug('amazon-com-aktie')).toBe(true);
    expect(isValidFinanzenNetSlug('3m-company-aktie')).toBe(true);
    expect(isValidFinanzenNetSlug('3m-aktie')).toBe(true);
  });

  it('accepts a single character slug (minimum length)', () => {
    expect(isValidFinanzenNetSlug('a')).toBe(true);
    expect(isValidFinanzenNetSlug('1')).toBe(true);
  });

  it('rejects a slug that starts with a hyphen', () => {
    expect(isValidFinanzenNetSlug('-bad-start')).toBe(false);
  });

  it('rejects a slug with uppercase letters', () => {
    expect(isValidFinanzenNetSlug('Microsoft-Aktie')).toBe(false);
  });

  it('rejects a slug with spaces', () => {
    expect(isValidFinanzenNetSlug('microsoft aktie')).toBe(false);
  });

  it('rejects a slug with special characters', () => {
    expect(isValidFinanzenNetSlug('microsoft.aktie')).toBe(false);
    expect(isValidFinanzenNetSlug('microsoft/aktie')).toBe(false);
  });

  it('accepts a slug with underscores', () => {
    expect(isValidFinanzenNetSlug('western_digital-aktie')).toBe(true);
    expect(isValidFinanzenNetSlug('some_slug_with_underscores')).toBe(true);
  });

  it('rejects a slug that starts with an underscore', () => {
    expect(isValidFinanzenNetSlug('_microsoft-aktie')).toBe(false);
  });

  it('rejects an empty string', () => {
    expect(isValidFinanzenNetSlug('')).toBe(false);
  });

  it('rejects null and undefined', () => {
    expect(isValidFinanzenNetSlug(null)).toBe(false);
    expect(isValidFinanzenNetSlug(undefined)).toBe(false);
  });

  it('rejects slugs longer than 120 characters', () => {
    const longSlug = 'a'.repeat(121);
    expect(isValidFinanzenNetSlug(longSlug)).toBe(false);
    const maxSlug = 'a'.repeat(120);
    expect(isValidFinanzenNetSlug(maxSlug)).toBe(true);
  });
});

describe('buildFinanzenNetUrl', () => {
  it('constructs the correct URL for a slug with underscores', () => {
    expect(buildFinanzenNetUrl('western_digital-aktie')).toBe(
      'https://www.finanzen.net/aktien/western_digital-aktie',
    );
  });

  it('constructs the correct URL for a valid slug', () => {
    expect(buildFinanzenNetUrl('microsoft-aktie')).toBe(
      'https://www.finanzen.net/aktien/microsoft-aktie',
    );
  });

  it('encodes the slug when constructing the URL', () => {
    // Slugs with only valid chars don't change after encodeURIComponent;
    // this also confirms the function uses encoding.
    expect(buildFinanzenNetUrl('amazon-com-aktie')).toBe(
      'https://www.finanzen.net/aktien/amazon-com-aktie',
    );
  });

  it('returns null for null slug', () => {
    expect(buildFinanzenNetUrl(null)).toBeNull();
  });

  it('returns null for undefined slug', () => {
    expect(buildFinanzenNetUrl(undefined)).toBeNull();
  });

  it('returns null for an empty string', () => {
    expect(buildFinanzenNetUrl('')).toBeNull();
  });

  it('returns null for an invalid slug with uppercase', () => {
    expect(buildFinanzenNetUrl('Microsoft-Aktie')).toBeNull();
  });

  it('returns null for an invalid slug with special characters', () => {
    expect(buildFinanzenNetUrl('microsoft/aktie')).toBeNull();
  });
});
