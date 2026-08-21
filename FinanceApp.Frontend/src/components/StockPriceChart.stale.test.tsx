// @vitest-environment jsdom
import React from 'react';
import userEvent from '@testing-library/user-event';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import StockPriceChart from './StockPriceChart';
import * as api from '../services/api';

vi.mock('../services/api', () => ({
  getStockHistory: vi.fn(),
  refreshStockHistory: vi.fn(),
  getIndexConstituentHistory: vi.fn(),
}));

vi.mock('./StockTechnicalAnalysisPanel', () => ({
  default: () => <div data-testid="technical-analysis-panel" />,
}));

const baseHistoryResponse = {
  range: '24h',
  interval: '10m',
  currency: 'USD',
  financialCurrency: 'USD',
  normalizedQuoteCurrency: 'USD',
  quoteUnitMultiplier: 1,
  rateToEur: null,
  rateTimestampUtc: null,
  rateSource: null,
  conversionWarning: null,
  asOfUtc: '2026-08-20T14:40:00Z',
  currentSessionHasCandles: true,
  isPotentiallyStale: true,
  staleReason: 'Данные устарели, показаны сохранённые свечи.',
  unavailableReason: null,
  volumeMetrics: {
    averageVolume20: null,
    averageVolume50: null,
    relativeVolume: null,
    turnover: null,
    turnoverCurrency: null,
    latestMetricsTimestamp: null,
    usesCompletedCandle: true,
  },
  points: [
    {
      timestamp: '2026-08-20T14:40:00Z',
      interval: '10m',
      openRaw: 100,
      highRaw: 101,
      lowRaw: 99,
      closeRaw: 100,
      openNormalized: 100,
      highNormalized: 101,
      lowNormalized: 99,
      closeNormalized: 100,
      openEur: null,
      highEur: null,
      lowEur: null,
      closeEur: null,
      volume: 1000,
      isQuoteDerived: false,
    },
  ],
} as const;

describe('StockPriceChart stale/unavailable diagnostics', () => {
  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it('renders as-of timestamp, stale warning, and ignores quote from another listing', async () => {
    vi.mocked(api.getStockHistory).mockResolvedValueOnce({ data: baseHistoryResponse } as never);

    render(
      <StockPriceChart
        panelId="p1"
        stockId={1}
        ticker="AMD"
        name="AMD Frankfurt"
        exchange="Frankfurt"
        providerSymbol="AMD.F"
        liveQuote={{
          symbol: 'AMD',
          rawCurrentPrice: 180,
          rawPreviousClose: 175,
          rawChange: 5,
          normalizedCurrentPrice: 180,
          normalizedPreviousClose: 175,
          normalizedChange: 5,
          currentPriceEur: null,
          changeEur: null,
          changePercent: 2.8,
          rawDayHigh: null,
          rawDayLow: null,
          normalizedDayHigh: null,
          normalizedDayLow: null,
          dayHighEur: null,
          dayLowEur: null,
          marketState: 'REGULAR',
          priceSession: 'REGULAR',
          priceTimestampUtc: '2026-08-20T15:00:00Z',
          isStale: false,
          currency: 'USD',
          financialCurrency: 'USD',
          normalizedQuoteCurrency: 'USD',
          quoteUnitMultiplier: 1,
          delayWarning: null,
          priceSource: null,
          rateToEur: null,
          rateTimestampUtc: null,
          rateSource: null,
          conversionWarning: null,
        }}
      />,
    );

    await waitFor(() => expect(vi.mocked(api.getStockHistory)).toHaveBeenCalled());
    expect(screen.getByText(/Данные на:/)).toBeInTheDocument();
    expect(screen.getByText('Данные устарели, показаны сохранённые свечи.')).toBeInTheDocument();
    expect(screen.getByText(/не совпадает с выбранным листингом AMD\.F/)).toBeInTheDocument();
  });

  it('shows actionable unavailable reason instead of generic empty chart text', async () => {
    vi.mocked(api.getStockHistory).mockResolvedValueOnce({
      data: {
        ...baseHistoryResponse,
        isPotentiallyStale: true,
        points: [],
        unavailableReason: 'Для листинга AMD (NASDAQ) нет данных за диапазон «24h». Проверьте биржу/тикер и попробуйте «Перезагрузить историю».',
      },
    } as never);

    render(
      <StockPriceChart
        panelId="p2"
        stockId={2}
        ticker="AMD"
        name="AMD US"
        exchange="NASDAQ"
        providerSymbol="AMD"
      />,
    );

    await waitFor(() => expect(vi.mocked(api.getStockHistory)).toHaveBeenCalled());
    expect(screen.getByText(/Проверьте биржу\/тикер и попробуйте «Перезагрузить историю»/)).toBeInTheDocument();
  });

  it('shows explicit no-current-session-candles and quote-derived diagnostics', async () => {
    const user = userEvent.setup();
    vi.mocked(api.getStockHistory).mockResolvedValue({
      data: {
        ...baseHistoryResponse,
        currentSessionHasCandles: false,
        points: [
          {
            ...baseHistoryResponse.points[0],
            isQuoteDerived: true,
          },
        ],
      },
    } as never);

    render(
      <StockPriceChart
        panelId="p3"
        stockId={3}
        ticker="AMD"
        name="AMD US"
        exchange="NASDAQ"
        providerSymbol="AMD"
      />,
    );

    await waitFor(() => expect(vi.mocked(api.getStockHistory)).toHaveBeenCalled());
    await user.click(screen.getByText('24 ч.'));
    expect(screen.getByText(/текущая торговая сессия ещё не содержит свечей/i)).toBeInTheDocument();
    expect(screen.getByText(/часть внутридневных точек получена из котировок/i)).toBeInTheDocument();
  });
});
