import type {
  TechnicalAnalysisFactor,
  TechnicalAnalysisHorizonResult,
  TechnicalAnalysisResponse,
  TechnicalAnalysisSignal,
} from '../types';

export type TechnicalAnalysisHorizonKey = 'threeMonths' | 'sixMonths' | 'oneYear' | 'twoYears';

export const TECHNICAL_ANALYSIS_HORIZON_STORAGE_KEY = 'financeapp:technical-analysis:horizon:v1';

export const TECHNICAL_ANALYSIS_INSUFFICIENT_DATA_TEXT = 'Недостаточно данных';

export const TECHNICAL_ANALYSIS_HORIZON_OPTIONS: Array<{ key: TechnicalAnalysisHorizonKey; label: string }> = [
  { key: 'threeMonths', label: '3 месяца' },
  { key: 'sixMonths', label: '6 месяцев' },
  { key: 'oneYear', label: '1 год' },
  { key: 'twoYears', label: '2 года' },
];

const KNOWN_HORIZON_KEYS = new Set<TechnicalAnalysisHorizonKey>(TECHNICAL_ANALYSIS_HORIZON_OPTIONS.map((x) => x.key));

export const normalizeTechnicalAnalysisHorizon = (value: unknown): TechnicalAnalysisHorizonKey | null =>
  typeof value === 'string' && KNOWN_HORIZON_KEYS.has(value as TechnicalAnalysisHorizonKey)
    ? (value as TechnicalAnalysisHorizonKey)
    : null;

export const readPersistedTechnicalAnalysisHorizon = (): TechnicalAnalysisHorizonKey => {
  try {
    const parsed = normalizeTechnicalAnalysisHorizon(window.localStorage.getItem(TECHNICAL_ANALYSIS_HORIZON_STORAGE_KEY));
    return parsed ?? 'threeMonths';
  } catch {
    return 'threeMonths';
  }
};

export const persistTechnicalAnalysisHorizon = (horizon: TechnicalAnalysisHorizonKey): void => {
  try {
    window.localStorage.setItem(TECHNICAL_ANALYSIS_HORIZON_STORAGE_KEY, horizon);
  } catch {
    // no-op when storage is unavailable
  }
};

const SIGNAL_LABELS: Record<TechnicalAnalysisSignal, string> = {
  StrongBullish: 'Сильный бычий',
  ModeratelyBullish: 'Умеренно бычий',
  Neutral: 'Нейтральный',
  ModeratelyBearish: 'Умеренно медвежий',
  StrongBearish: 'Сильный медвежий',
};

const SIGNAL_COLORS: Record<TechnicalAnalysisSignal, string> = {
  StrongBullish: '#135200',
  ModeratelyBullish: '#389e0d',
  Neutral: '#d48806',
  ModeratelyBearish: '#d46b08',
  StrongBearish: '#a8071a',
};

export const getSignalLabel = (signal: TechnicalAnalysisSignal): string => SIGNAL_LABELS[signal] ?? signal;

export const getSignalColor = (signal: TechnicalAnalysisSignal): string => SIGNAL_COLORS[signal] ?? '#595959';

export const getHorizonResult = (
  response: TechnicalAnalysisResponse,
  horizon: TechnicalAnalysisHorizonKey,
): TechnicalAnalysisHorizonResult => response[horizon];

const FACTOR_MESSAGE_BY_CODE: Partial<Record<string, string>> = {
  PRICE_ABOVE_SMA50: 'Цена выше SMA50.',
  PRICE_BELOW_SMA50: 'Цена ниже SMA50.',
  PRICE_ABOVE_SMA200: 'Цена выше SMA200.',
  PRICE_BELOW_SMA200: 'Цена ниже SMA200.',
  MA_ORDER_BULLISH: 'Скользящие средние указывают на бычий порядок.',
  MA_ORDER_BEARISH: 'Скользящие средние указывают на медвежий порядок.',
  SMA20_ABOVE_SMA50: 'SMA20 выше SMA50.',
  SMA20_BELOW_SMA50: 'SMA20 ниже SMA50.',
  RSI_BULLISH_RANGE: 'RSI в бычьем диапазоне.',
  RSI_OVERBOUGHT: 'RSI в зоне перекупленности.',
  RSI_WEAK: 'RSI указывает на ослабление импульса.',
  RSI_OVERSOLD: 'RSI в зоне перепроданности.',
  MACD_HISTOGRAM_POSITIVE: 'Гистограмма MACD положительная.',
  MACD_HISTOGRAM_NEGATIVE: 'Гистограмма MACD отрицательная.',
  EMA_BULLISH: 'EMA12 выше EMA26.',
  EMA_BEARISH: 'EMA12 ниже EMA26.',
  RETURN_POSITIVE: 'Доходность положительная.',
  RETURN_NEGATIVE: 'Доходность отрицательная.',
  VOLATILITY_MODERATE: 'Волатильность умеренная.',
  VOLATILITY_HIGH: 'Волатильность высокая.',
  VOLATILITY_ELEVATED: 'Волатильность повышенная.',
  DRAWDOWN_CONTAINED: 'Просадка контролируемая.',
  DRAWDOWN_ELEVATED: 'Просадка повышенная.',
  DRAWDOWN_SEVERE: 'Просадка выраженная.',
  ATR_CONTAINED: 'ATR в допустимом диапазоне.',
  ATR_ELEVATED: 'ATR повышен.',
  FUNDAMENTALS_MISSING: 'Фундаментальные данные отсутствуют.',
  FUNDAMENTALS_STALE: 'Фундаментальные данные устарели.',
  FUNDAMENTALS_UNUSABLE: 'Фундаментальные данные непригодны для оценки.',
  FUNDAMENTAL_HISTORY_INSUFFICIENT: 'Недостаточно фундаментальной истории.',
  HISTORY_MISSING: 'История котировок отсутствует.',
  HISTORY_STALE: 'История котировок устарела.',
  HISTORY_INSUFFICIENT: 'Недостаточно исторических данных.',
  SMA200_UNAVAILABLE: 'SMA200 недоступна из-за недостаточной истории.',
  ADJUSTED_CLOSE_INCOMPLETE: 'Покрытие AdjustedClose неполное.',
  ADJUSTED_CLOSE_FALLBACK: 'Использован fallback с AdjustedClose на Close.',
  COMPONENTS_MISSING: 'Часть компонент оценки недоступна.',
  DUPLICATE_CANDLES: 'Обнаружены дублирующиеся свечи.',
  INVALID_CLOSE_POINTS: 'Обнаружены некорректные значения Close.',
  CONSTANT_PRICE_SERIES: 'Серия цен постоянная.',
};

export const localizeFactor = (factor: TechnicalAnalysisFactor): { code: string; primaryMessage: string; fallbackMessage: string | null } => {
  const localized = FACTOR_MESSAGE_BY_CODE[factor.code];
  const serverMessage = factor.message?.trim() ?? '';

  if (localized) {
    return {
      code: factor.code,
      primaryMessage: localized,
      fallbackMessage: serverMessage.length > 0 && serverMessage !== localized ? serverMessage : null,
    };
  }

  return {
    code: factor.code,
    primaryMessage: serverMessage.length > 0 ? serverMessage : 'Сообщение отсутствует.',
    fallbackMessage: null,
  };
};

export const dedupeFactors = (factors: TechnicalAnalysisFactor[]): TechnicalAnalysisFactor[] => {
  const seen = new Set<string>();
  const result: TechnicalAnalysisFactor[] = [];

  for (const factor of factors) {
    const key = `${factor.code}\u0000${factor.message}`;
    if (seen.has(key)) continue;
    seen.add(key);
    result.push(factor);
  }

  return result;
};

const isFiniteNumber = (value: number | null | undefined): value is number =>
  typeof value === 'number' && Number.isFinite(value);

export const formatTechnicalScore = (value: number | null | undefined): string =>
  isFiniteNumber(value) ? String(Math.round(value)) : TECHNICAL_ANALYSIS_INSUFFICIENT_DATA_TEXT;

export const formatPercentPoints = (
  value: number | null | undefined,
  fractionDigits = 1,
): string => (isFiniteNumber(value)
  ? `${new Intl.NumberFormat('ru-RU', { maximumFractionDigits: fractionDigits }).format(value)}%`
  : TECHNICAL_ANALYSIS_INSUFFICIENT_DATA_TEXT);

export const formatFractionAsPercent = (
  value: number | null | undefined,
  fractionDigits = 0,
): string => (isFiniteNumber(value)
  ? `${new Intl.NumberFormat('ru-RU', { maximumFractionDigits: fractionDigits }).format(value * 100)}%`
  : TECHNICAL_ANALYSIS_INSUFFICIENT_DATA_TEXT);

export const formatTechnicalNumber = (
  value: number | null | undefined,
  fractionDigits = 2,
): string => (isFiniteNumber(value)
  ? new Intl.NumberFormat('ru-RU', { maximumFractionDigits: fractionDigits }).format(value)
  : '—');
