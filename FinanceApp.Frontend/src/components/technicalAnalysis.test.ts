import { describe, expect, it } from 'vitest';
import {
  formatFractionAsPercent,
  formatPercentPoints,
  formatTechnicalNumber,
  getSignalLabel,
  localizeFactor,
  normalizeTechnicalAnalysisHorizon,
} from './technicalAnalysis';

describe('technical analysis helpers', () => {
  it('maps all five backend signals to russian labels', () => {
    expect(getSignalLabel('StrongBullish')).toBe('Сильный бычий');
    expect(getSignalLabel('ModeratelyBullish')).toBe('Умеренно бычий');
    expect(getSignalLabel('Neutral')).toBe('Нейтральный');
    expect(getSignalLabel('ModeratelyBearish')).toBe('Умеренно медвежий');
    expect(getSignalLabel('StrongBearish')).toBe('Сильный медвежий');
  });

  it('validates persisted horizon values', () => {
    expect(normalizeTechnicalAnalysisHorizon('threeMonths')).toBe('threeMonths');
    expect(normalizeTechnicalAnalysisHorizon('sixMonths')).toBe('sixMonths');
    expect(normalizeTechnicalAnalysisHorizon('invalid')).toBeNull();
    expect(normalizeTechnicalAnalysisHorizon(null)).toBeNull();
  });

  it('formats api percentage units without double multiplication', () => {
    expect(formatPercentPoints(8.7)).toBe('8,7%');
    expect(formatPercentPoints(-18)).toBe('-18%');
    expect(formatFractionAsPercent(0.42)).toBe('42%');
  });

  it('handles null and invalid numeric values', () => {
    expect(formatPercentPoints(null)).toBe('Недостаточно данных');
    expect(formatPercentPoints(Number.NaN)).toBe('Недостаточно данных');
    expect(formatFractionAsPercent(Number.POSITIVE_INFINITY)).toBe('Недостаточно данных');
    expect(formatTechnicalNumber(undefined)).toBe('—');
  });

  it('localizes known factor code and falls back for unknown', () => {
    const known = localizeFactor({ code: 'RSI_OVERSOLD', message: 'RSI is oversold.' });
    expect(known.primaryMessage).toContain('перепроданности');
    expect(known.fallbackMessage).toBe('RSI is oversold.');

    const unknown = localizeFactor({ code: 'UNKNOWN_CODE', message: 'Server supplied detail 42.' });
    expect(unknown.primaryMessage).toBe('Server supplied detail 42.');
    expect(unknown.code).toBe('UNKNOWN_CODE');
  });
});
