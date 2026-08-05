import { describe, expect, it } from 'vitest';
import {
  SUMMARY_CARD_SIZE,
  SUMMARY_ROW_GUTTER,
  SUMMARY_ROW_MARGIN_BOTTOM,
} from './PortfolioDetailPage';

describe('PortfolioDetailPage – compact summary cards', () => {
  it('uses small Ant Design Card size for summary cards', () => {
    expect(SUMMARY_CARD_SIZE).toBe('small');
  });

  it('uses reduced vertical gutter (8 px) between summary rows', () => {
    expect(SUMMARY_ROW_GUTTER[1]).toBe(8);
  });

  it('keeps horizontal gutter unchanged at 16 px', () => {
    expect(SUMMARY_ROW_GUTTER[0]).toBe(16);
  });

  it('reduces bottom margin of summary section to 12 px (was 24 px)', () => {
    expect(SUMMARY_ROW_MARGIN_BOTTOM).toBe(12);
    expect(SUMMARY_ROW_MARGIN_BOTTOM).toBeLessThan(24);
  });
});
