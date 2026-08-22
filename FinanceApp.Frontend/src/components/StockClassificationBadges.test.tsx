// @vitest-environment jsdom
import React from 'react';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import StockClassificationBadges, { buildClassificationText, buildCompactClassificationCode } from './StockClassificationBadges';

describe('StockClassificationBadges', () => {
  afterEach(() => {
    cleanup();
  });

  it('maps all known sector names to compact codes', () => {
    const cases: Array<[string, string]> = [
      ['Communication Services', 'COM'],
      ['Consumer Discretionary', 'CD'],
      ['Consumer Staples', 'CS'],
      ['Energy', 'EN'],
      ['Financials', 'FIN'],
      ['Health Care', 'HC'],
      ['Industrials', 'IND'],
      ['Information Technology', 'IT'],
      ['Materials', 'MAT'],
      ['Real Estate', 'RE'],
      ['Utilities', 'UTIL'],
    ];

    for (const [sector, code] of cases) {
      expect(buildCompactClassificationCode(sector, null)).toBe(code);
    }
  });

  it('uses deterministic compact fallback for unknown sector', () => {
    expect(buildCompactClassificationCode('Future Quantum Services', null)).toBe('FQS');
  });

  it('uses compact fallback from industry when sector is absent', () => {
    expect(buildCompactClassificationCode(null, 'Restaurants')).toBe('REST');
  });

  it('renders compact code only while preserving full text in title and aria', () => {
    render(<StockClassificationBadges sector="Information Technology" industry="Software" />);

    expect(screen.getByText('IT')).toBeInTheDocument();
    expect(screen.queryByText('Information Technology')).not.toBeInTheDocument();
    const classification = screen.getByLabelText('Классификация: Information Technology · Software');
    expect(classification).toHaveAttribute('title', 'Information Technology · Software');
  });

  it('uses muted plain typography and no legacy tags', () => {
    render(<StockClassificationBadges sector="Financials" industry={null} />);
    const classification = screen.getByText('FIN');
    expect(classification).toHaveStyle({ fontSize: '14px', textAlign: 'right', marginLeft: 'auto' });
    expect(screen.queryByText('СЕК')).not.toBeInTheDocument();
    expect(screen.queryByText('ОТР')).not.toBeInTheDocument();
  });

  it('returns expected full classification text helper output', () => {
    expect(buildClassificationText('Information Technology', 'Software')).toBe('Information Technology · Software');
    expect(buildClassificationText('Information Technology', null)).toBe('Information Technology');
    expect(buildClassificationText(null, 'Software')).toBe('Software');
    expect(buildClassificationText(null, null)).toBeNull();
  });

  it('does not render anything when both values are absent', () => {
    const { container } = render(<StockClassificationBadges sector={null} industry={null} />);
    expect(container).toBeEmptyDOMElement();
  });
});
