// @vitest-environment jsdom
import React from 'react';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import StockClassificationBadges from './StockClassificationBadges';

describe('StockClassificationBadges', () => {
  afterEach(() => {
    cleanup();
  });

  it('renders sector text without hover-only placeholders', () => {
    render(<StockClassificationBadges sector="Information Technology" industry={null} />);
    expect(screen.getByText('Information Technology')).toBeInTheDocument();
    expect(screen.queryByText('СЕК')).not.toBeInTheDocument();
    expect(screen.queryByText('ОТР')).not.toBeInTheDocument();
  });

  it('renders sector and industry with a middle dot separator', () => {
    render(<StockClassificationBadges sector="Information Technology" industry="Software" />);
    expect(screen.getByText('Information Technology · Software')).toBeInTheDocument();
  });

  it('renders industry-only text without a leading separator', () => {
    render(<StockClassificationBadges sector={null} industry="Software" />);
    expect(screen.getByText('Software')).toBeInTheDocument();
    expect(screen.queryByText('· Software')).not.toBeInTheDocument();
  });

  it('provides full text via title and aria label', () => {
    render(<StockClassificationBadges sector="Information Technology" industry="Software" />);
    const classification = screen.getByLabelText('Классификация: Information Technology · Software');
    expect(classification).toHaveAttribute('title', 'Information Technology · Software');
    expect(classification).toHaveStyle({
      marginLeft: 'auto',
      textAlign: 'right',
      fontSize: '14px',
    });
  });

  it('does not render anything when both values are absent', () => {
    const { container } = render(<StockClassificationBadges sector={null} industry={null} />);
    expect(container).toBeEmptyDOMElement();
  });
});
