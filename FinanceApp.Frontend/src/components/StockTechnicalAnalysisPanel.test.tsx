// @vitest-environment jsdom
import React from 'react';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axios from 'axios';
import StockTechnicalAnalysisPanel from './StockTechnicalAnalysisPanel';
import { TECHNICAL_ANALYSIS_HORIZON_STORAGE_KEY } from './technicalAnalysis';
import type { TechnicalAnalysisResponse } from '../types';
import { MemoryRouter } from 'react-router-dom';

const getStockTechnicalAnalysisMock = vi.fn();

vi.mock('../services/api', () => ({
  getStockTechnicalAnalysis: (...args: unknown[]) => getStockTechnicalAnalysisMock(...args),
}));

const baseResponse: TechnicalAnalysisResponse = {
  stockId: 7,
  ticker: 'AAA',
  name: 'Alpha',
  commonName: 'Alpha',
  exchange: 'NASDAQ',
  isin: null,
  wkn: null,
  asOfUtc: '2026-08-19T00:00:00Z',
  isPotentiallyStale: true,
  historyRefreshCadence: 'Daily',
  lastIncrementalHistoryRefreshSucceededAtUtc: null,
  nextIncrementalHistoryRefreshAtUtc: null,
  lastHistoryReconciliationSucceededAtUtc: null,
  nextHistoryReconciliationAtUtc: null,
  lastFullHistoryBackfillSucceededAtUtc: null,
  nextFullHistoryBackfillAtUtc: null,
  metrics: {
    latestPrice: 123.456,
    dailyCandleCount: 280,
    adjustedCloseCoverage: 0.8,
    sma20: 120,
    sma50: 118,
    sma200: null,
    ema12: 121,
    ema26: 119,
    rsi14: 54.2,
    macd: 1.1,
    macdSignal: 0.8,
    macdHistogram: 0.3,
    return1Month: 4.2,
    return3Months: 8.7,
    return6Months: 12.4,
    return1Year: 18.9,
    volatilityAnnualized20: 0.24,
    volatilityAnnualized60: 0.31,
    maxDrawdown: -18,
    atr14: 2.42,
    priceBasis: [{ metric: 'CloseBasedIndicators', basis: 'AdjustedClosePreferredWithPerPointCloseFallback', reason: 'Each candle uses AdjustedClose when valid; otherwise Close.' }],
  },
  warnings: [{ code: 'HISTORY_STALE', message: 'Latest candle is stale.' }],
  threeMonths: {
    score: 72.4,
    signal: 'ModeratelyBullish',
    confidence: 0.91,
    componentScores: { trend: 71, momentum: 75, returns: 69, risk: 65, fundamentals: null },
    componentWeights: { trend: 0.35, momentum: 0.35, returns: 0.2, risk: 0.1, fundamentals: 0 },
    positiveFactors: [{ code: 'PRICE_ABOVE_SMA50', message: 'Price is above SMA50.' }],
    negativeFactors: [{ code: 'RSI_OVERBOUGHT', message: 'RSI overbought.' }],
    warnings: [{ code: 'FUNDAMENTALS_MISSING', message: 'Fundamentals unavailable.' }],
  },
  sixMonths: {
    score: 63,
    signal: 'Neutral',
    confidence: 0.45,
    componentScores: { trend: 62, momentum: 58, returns: 64, risk: 60, fundamentals: null },
    componentWeights: { trend: 0.3, momentum: 0.2, returns: 0.25, risk: 0.15, fundamentals: 0.1 },
    positiveFactors: [],
    negativeFactors: [],
    warnings: [],
  },
  oneYear: {
    score: 42,
    signal: 'ModeratelyBearish',
    confidence: 0.74,
    componentScores: { trend: 40, momentum: 38, returns: 44, risk: 55, fundamentals: 36 },
    componentWeights: { trend: 0.3, momentum: 0.15, returns: 0.2, risk: 0.15, fundamentals: 0.2 },
    positiveFactors: [{ code: 'RETURN_POSITIVE', message: 'Return is positive.' }],
    negativeFactors: [{ code: 'VOLATILITY_HIGH', message: 'Volatility high.' }],
    warnings: [],
  },
  twoYears: {
    score: 22,
    signal: 'StrongBearish',
    confidence: 0.3,
    componentScores: { trend: 30, momentum: null, returns: 28, risk: 25, fundamentals: null },
    componentWeights: { trend: 0.15, momentum: 0.05, returns: 0.15, risk: 0.2, fundamentals: 0.45 },
    positiveFactors: [],
    negativeFactors: [{ code: 'UNKNOWN_CUSTOM', message: 'Dynamic server message 123%' }],
    warnings: [{ code: 'HISTORY_INSUFFICIENT', message: 'History coverage for TwoYears is 45%.' }],
  },
};

describe('StockTechnicalAnalysisPanel', () => {
  afterEach(() => {
    cleanup();
  });

  beforeEach(() => {
    vi.clearAllMocks();
    window.localStorage.clear();
    getStockTechnicalAnalysisMock.mockResolvedValue({ data: baseResponse });
  });

  it('renders four horizon controls, defaults to 3 months and shows score/confidence separately', async () => {
    render(
      <MemoryRouter>
        <StockTechnicalAnalysisPanel stockId={7} />
      </MemoryRouter>,
    );

    expect(screen.getByRole('status', { name: 'Загрузка аналитического сигнала' })).toBeInTheDocument();
    await screen.findByText('Аналитический сигнал');

    expect(screen.getByRole('tab', { name: '3 месяца' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: '6 месяцев' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: '1 год' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: '2 года' })).toBeInTheDocument();

    expect(screen.getByText('72')).toBeInTheDocument();
    expect(screen.getByText('91%')).toBeInTheDocument();
    expect(screen.getByText('Умеренно бычий')).toBeInTheDocument();
    expect(screen.getByText('Данные аналитического сигнала могут быть устаревшими.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Открыть справку по аналитическому сигналу' })).toHaveAttribute('href', '/help/analytical-signal#signal-location');
  });

  it('restores valid local-storage horizon, falls back on malformed value, and persists selection', async () => {
    window.localStorage.setItem(TECHNICAL_ANALYSIS_HORIZON_STORAGE_KEY, 'twoYears');
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <StockTechnicalAnalysisPanel stockId={7} />
      </MemoryRouter>,
    );
    await screen.findByText('Сильный медвежий');
    expect(screen.getByRole('tab', { name: '2 года' })).toHaveAttribute('aria-selected', 'true');

    await user.click(screen.getByRole('tab', { name: '6 месяцев' }));
    expect(window.localStorage.getItem(TECHNICAL_ANALYSIS_HORIZON_STORAGE_KEY)).toBe('sixMonths');

    cleanup();
    window.localStorage.setItem(TECHNICAL_ANALYSIS_HORIZON_STORAGE_KEY, 'bad-value');
    render(
      <MemoryRouter>
        <StockTechnicalAnalysisPanel stockId={8} />
      </MemoryRouter>,
    );
    expect(await screen.findByRole('tab', { name: '3 месяца' })).toHaveAttribute('aria-selected', 'true');
  });

  it('renders component scores and weights, showing null score as insufficient data and 0% fundamentals weight', async () => {
    render(
      <MemoryRouter>
        <StockTechnicalAnalysisPanel stockId={7} />
      </MemoryRouter>,
    );

    await screen.findByText('Компоненты');
    expect(screen.getByText('Вес: 0%')).toBeInTheDocument();
    expect(screen.getByText('Недостаточно данных')).toBeInTheDocument();
    expect(screen.getByText('71 / 100')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Открыть методику расчёта фундаментального компонента' }))
      .toHaveAttribute('href', '/help/fundamental-scoring-methodology#fundamental-methodology-component-calculation');
  });

  it('shows low confidence warning and factor localization/fallback', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <StockTechnicalAnalysisPanel stockId={7} />
      </MemoryRouter>,
    );

    await user.click(await screen.findByRole('tab', { name: '6 месяцев' }));
    expect(screen.getByText('Низкая уверенность сигнала: 45%.')).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: '2 года' }));
    expect(screen.getByText('Dynamic server message 123%')).toBeInTheDocument();
    expect(screen.getByText('UNKNOWN_CUSTOM')).toBeInTheDocument();
  });

  it('renders loading and retryable server error state', async () => {
    const user = userEvent.setup();
    getStockTechnicalAnalysisMock
      .mockRejectedValueOnce({ response: { status: 500 } })
      .mockResolvedValueOnce({ data: baseResponse });

    render(
      <MemoryRouter>
        <StockTechnicalAnalysisPanel stockId={7} />
      </MemoryRouter>,
    );
    expect(await screen.findByText('Не удалось загрузить аналитический сигнал. Попробуйте снова.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Повторить' }));
    expect(await screen.findByText('Умеренно бычий')).toBeInTheDocument();
  });

  it('renders 404 state and keeps selector keyboard-focusable', async () => {
    getStockTechnicalAnalysisMock.mockRejectedValueOnce({ response: { status: 404 } });
    render(
      <MemoryRouter>
        <StockTechnicalAnalysisPanel stockId={999} />
      </MemoryRouter>,
    );

    expect(await screen.findByRole('alert')).toHaveTextContent('Аналитический сигнал недоступен: акция не найдена.');

    cleanup();
    getStockTechnicalAnalysisMock.mockResolvedValueOnce({ data: baseResponse });
    render(
      <MemoryRouter>
        <StockTechnicalAnalysisPanel stockId={7} />
      </MemoryRouter>,
    );
    const tab = await screen.findByRole('tab', { name: '1 год' });
    tab.focus();
    expect(document.activeElement).toBe(tab);
  });

  it('formats metrics and links the disclosure to the exact formula methodology section', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <StockTechnicalAnalysisPanel stockId={7} />
      </MemoryRouter>,
    );

    const disclosure = await screen.findByText('Показатели');
    await user.click(disclosure);

    await waitFor(() => {
      expect(screen.getByText('24% / 31%')).toBeInTheDocument();
      expect(screen.getByText('4,2% / 8,7% / 12,4% / 18,9%')).toBeInTheDocument();
      expect(screen.getByText('80%')).toBeInTheDocument();
    });

    expect(screen.getByRole('link', { name: 'Открыть справку о формулах технических показателей' }))
      .toHaveAttribute('href', '/help/technical-indicator-formulas#indicator-methodology');
    expect(getStockTechnicalAnalysisMock).toHaveBeenCalledTimes(1);
  });

  it('handles aborted request safely', async () => {
    getStockTechnicalAnalysisMock.mockImplementation(async (_stockId: number, signal?: AbortSignal) => {
      await new Promise((resolve) => setTimeout(resolve, 1));
      if (signal?.aborted) {
        throw new axios.CanceledError('aborted');
      }
      return { data: baseResponse };
    });

    const { unmount } = render(
      <MemoryRouter>
        <StockTechnicalAnalysisPanel stockId={7} />
      </MemoryRouter>,
    );
    unmount();
    await waitFor(() => expect(getStockTechnicalAnalysisMock).toHaveBeenCalledTimes(1));
  });
});
