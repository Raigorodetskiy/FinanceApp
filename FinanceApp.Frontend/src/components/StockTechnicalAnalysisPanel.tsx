import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Alert, Button, Spin, Tag, Typography, Progress } from 'antd';
import axios from 'axios';
import dayjs from 'dayjs';
import utc from 'dayjs/plugin/utc';
import { Link } from 'react-router-dom';
import { getStockTechnicalAnalysis } from '../services/api';
import type {
  TechnicalAnalysisComponentScores,
  TechnicalAnalysisComponentWeights,
  TechnicalAnalysisFactor,
  TechnicalAnalysisResponse,
} from '../types';
import {
  dedupeFactors,
  formatFractionAsPercent,
  formatPercentPoints,
  formatTechnicalNumber,
  formatTechnicalScore,
  getHorizonResult,
  getSignalColor,
  getSignalLabel,
  localizeFactor,
  persistTechnicalAnalysisHorizon,
  readPersistedTechnicalAnalysisHorizon,
  TECHNICAL_ANALYSIS_HORIZON_OPTIONS,
  TECHNICAL_ANALYSIS_INSUFFICIENT_DATA_TEXT,
  type TechnicalAnalysisHorizonKey,
} from './technicalAnalysis';
import './StockTechnicalAnalysisPanel.css';

dayjs.extend(utc);

const { Text } = Typography;

const ANALYSIS_TIME_FORMAT = 'DD.MM.YYYY HH:mm';
const LOW_CONFIDENCE_THRESHOLD = 0.5;

const COMPONENT_DEFINITIONS: Array<{
  key: keyof TechnicalAnalysisComponentScores;
  label: string;
}> = [
  { key: 'trend', label: 'Тренд' },
  { key: 'momentum', label: 'Моментум' },
  { key: 'returns', label: 'Доходность' },
  { key: 'risk', label: 'Риск' },
  { key: 'fundamentals', label: 'Фундаментальные' },
];

type PanelState =
  | { kind: 'loading' }
  | { kind: 'error'; status: number | null; message: string }
  | { kind: 'success'; response: TechnicalAnalysisResponse };

const getAnalysisErrorMessage = (error: unknown): { status: number | null; message: string } => {
  const status =
    axios.isAxiosError(error)
      ? (error.response?.status ?? null)
      : (
        typeof error === 'object'
        && error != null
        && 'response' in error
        && typeof (error as { response?: { status?: unknown } }).response?.status === 'number'
      )
        ? ((error as { response: { status: number } }).response.status)
        : null;

  if (status === 404) {
    return { status, message: 'Аналитический сигнал недоступен: акция не найдена.' };
  }

  return {
    status,
    message: 'Не удалось загрузить аналитический сигнал. Попробуйте снова.',
  };
};

const renderFactorList = (
  title: string,
  factors: TechnicalAnalysisFactor[],
  emptyText: string,
): React.ReactElement => (
  <section className="technical-analysis-panel__factor-section" aria-label={title}>
    <h4 className="technical-analysis-panel__subheading">{title}</h4>
    {factors.length === 0 ? (
      <Text type="secondary">{emptyText}</Text>
    ) : (
      <ul className="technical-analysis-panel__factor-list">
        {factors.map((factor, idx) => {
          const localized = localizeFactor(factor);
          return (
            <li key={`${factor.code}-${idx}`}>
              <div className="technical-analysis-panel__factor-line">
                <Tag>{localized.code}</Tag>
                <span>{localized.primaryMessage}</span>
              </div>
              {localized.fallbackMessage && (
                <Text type="secondary" className="technical-analysis-panel__factor-fallback">
                  {localized.fallbackMessage}
                </Text>
              )}
            </li>
          );
        })}
      </ul>
    )}
  </section>
);

const formatAsOf = (asOfUtc: string | null): string =>
  asOfUtc ? dayjs.utc(asOfUtc).local().format(ANALYSIS_TIME_FORMAT) : TECHNICAL_ANALYSIS_INSUFFICIENT_DATA_TEXT;

const getComponentWeight = (weights: TechnicalAnalysisComponentWeights, key: keyof TechnicalAnalysisComponentScores): number => {
  switch (key) {
    case 'trend':
      return weights.trend;
    case 'momentum':
      return weights.momentum;
    case 'returns':
      return weights.returns;
    case 'risk':
      return weights.risk;
    case 'fundamentals':
      return weights.fundamentals;
    default:
      return 0;
  }
};

const StockTechnicalAnalysisPanel: React.FC<{ stockId: number }> = ({ stockId }) => {
  const [horizon, setHorizon] = useState<TechnicalAnalysisHorizonKey>(() => readPersistedTechnicalAnalysisHorizon());
  const [state, setState] = useState<PanelState>({ kind: 'loading' });
  const requestIdRef = useRef(0);

  const load = useCallback(async (signal?: AbortSignal) => {
    const requestId = ++requestIdRef.current;
    setState({ kind: 'loading' });

    try {
      const response = await getStockTechnicalAnalysis(stockId, signal);
      if (signal?.aborted || requestId !== requestIdRef.current) {
        return;
      }

      setState({ kind: 'success', response: response.data });
    } catch (error: unknown) {
      if (signal?.aborted || axios.isCancel(error) || requestId !== requestIdRef.current) {
        return;
      }

      const mapped = getAnalysisErrorMessage(error);
      if (mapped.status === 401) {
        return;
      }

      setState({ kind: 'error', ...mapped });
    }
  }, [stockId]);

  useEffect(() => {
    const abortController = new AbortController();
    void load(abortController.signal);
    return () => {
      abortController.abort();
    };
  }, [load]);

  const onHorizonClick = (next: TechnicalAnalysisHorizonKey) => {
    setHorizon(next);
    persistTechnicalAnalysisHorizon(next);
  };

  const selected = useMemo(() => (
    state.kind === 'success'
      ? getHorizonResult(state.response, horizon)
      : null
  ), [horizon, state]);

  const warnings = useMemo(() => {
    if (state.kind !== 'success' || selected == null) {
      return [];
    }

    return dedupeFactors([...(state.response.warnings ?? []), ...(selected.warnings ?? [])]);
  }, [selected, state]);

  return (
    <section className="technical-analysis-panel" aria-labelledby={`analysis-title-${stockId}`}>
      <div className="technical-analysis-panel__header">
        <h3 id={`analysis-title-${stockId}`} className="technical-analysis-panel__title">Аналитический сигнал</h3>
        <Link
          to="/help/analytical-signal#signal-location"
          className="technical-analysis-panel__help-link"
          aria-label="Открыть справку по аналитическому сигналу"
        >
          Как это работает
        </Link>
      </div>

      <div aria-live="polite" aria-atomic="true">
        {state.kind === 'loading' && (
          <div className="technical-analysis-panel__loading" role="status" aria-label="Загрузка аналитического сигнала">
            <Spin />
          </div>
        )}

        {state.kind === 'error' && (
          <Alert
            type="error"
            showIcon
            message={state.message}
            action={(
              <Button size="small" onClick={() => { void load(); }}>
                Повторить
              </Button>
            )}
          />
        )}
      </div>

      {state.kind === 'success' && selected && (
        <div className="technical-analysis-panel__body">
          <div className="technical-analysis-panel__selector" role="tablist" aria-label="Выбор горизонта аналитического сигнала">
            {TECHNICAL_ANALYSIS_HORIZON_OPTIONS.map((option) => {
              const selectedOption = option.key === horizon;
              return (
                <button
                  key={option.key}
                  type="button"
                  role="tab"
                  aria-selected={selectedOption}
                  aria-pressed={selectedOption}
                  className={`technical-analysis-panel__selector-button${selectedOption ? ' is-selected' : ''}`}
                  onClick={() => onHorizonClick(option.key)}
                >
                  {option.label}
                </button>
              );
            })}
          </div>

          <div className="technical-analysis-panel__summary-grid">
            <div className="technical-analysis-panel__summary-item">
              <Text type="secondary">Score</Text>
              <div className="technical-analysis-panel__summary-value">{formatTechnicalScore(selected.score)}</div>
            </div>
            <div className="technical-analysis-panel__summary-item">
              <Text type="secondary">Сигнал</Text>
              <Tag style={{ color: getSignalColor(selected.signal), borderColor: getSignalColor(selected.signal), background: '#fff' }}>
                {getSignalLabel(selected.signal)}
              </Tag>
            </div>
            <div className="technical-analysis-panel__summary-item">
              <Text type="secondary">Уверенность</Text>
              <div className="technical-analysis-panel__summary-value">{formatFractionAsPercent(selected.confidence, 0)}</div>
            </div>
            <div className="technical-analysis-panel__summary-item">
              <Text type="secondary">Данные на</Text>
              <div className="technical-analysis-panel__summary-value">{formatAsOf(state.response.asOfUtc)}</div>
            </div>
          </div>

          {state.response.isPotentiallyStale && (
            <Alert
              type="warning"
              showIcon
              message="Данные аналитического сигнала могут быть устаревшими."
            />
          )}

          {selected.confidence < LOW_CONFIDENCE_THRESHOLD && (
            <Alert
              type="warning"
              showIcon
              message={`Низкая уверенность сигнала: ${formatFractionAsPercent(selected.confidence, 0)}.`}
            />
          )}

          <section aria-label="Компоненты аналитического сигнала">
            <h4 className="technical-analysis-panel__subheading">Компоненты</h4>
            <div className="technical-analysis-panel__components-grid">
              {COMPONENT_DEFINITIONS.map((component) => {
                const score = selected.componentScores?.[component.key] ?? null;
                const weight = getComponentWeight(selected.componentWeights, component.key);
                const scoreText = formatTechnicalScore(score);
                const isMissing = score == null;

                return (
                  <article key={component.key} className="technical-analysis-panel__component-card">
                    <div className="technical-analysis-panel__component-header">
                      <Text strong>{component.label}</Text>
                      <Text type="secondary">Вес: {formatFractionAsPercent(weight, 0)}</Text>
                    </div>
                    {isMissing ? (
                      <Text type="secondary">{TECHNICAL_ANALYSIS_INSUFFICIENT_DATA_TEXT}</Text>
                    ) : (
                      <>
                        <Progress
                          percent={Math.max(0, Math.min(100, Math.round(score ?? 0)))}
                          size="small"
                          showInfo={false}
                          aria-label={`${component.label}: score ${scoreText} из 100`}
                        />
                        <Text>{scoreText} / 100</Text>
                      </>
                    )}
                  </article>
                );
              })}
            </div>
          </section>

          <div className="technical-analysis-panel__factors-grid">
            {renderFactorList('Положительные факторы', selected.positiveFactors, 'Положительные факторы отсутствуют.')}
            {renderFactorList('Факторы риска', selected.negativeFactors, 'Факторы риска отсутствуют.')}
          </div>

          {renderFactorList('Предупреждения', warnings, 'Предупреждения отсутствуют.')}

          <details className="technical-analysis-panel__metrics">
            <summary>Показатели</summary>
            <p>
              <Link
                to="/help/technical-indicator-formulas#indicator-methodology"
                aria-label="Открыть справку о формулах технических показателей"
              >
                Как рассчитываются показатели?
              </Link>
            </p>
            <div className="technical-analysis-panel__metrics-grid">
              <div><Text type="secondary">Последняя цена</Text><div>{formatTechnicalNumber(state.response.metrics.latestPrice)}</div></div>
              <div><Text type="secondary">Свечей (Daily)</Text><div>{formatTechnicalNumber(state.response.metrics.dailyCandleCount, 0)}</div></div>
              <div><Text type="secondary">AdjustedClose coverage</Text><div>{formatFractionAsPercent(state.response.metrics.adjustedCloseCoverage, 1)}</div></div>
              <div><Text type="secondary">SMA20 / SMA50 / SMA200</Text><div>{formatTechnicalNumber(state.response.metrics.sma20)} / {formatTechnicalNumber(state.response.metrics.sma50)} / {formatTechnicalNumber(state.response.metrics.sma200)}</div></div>
              <div><Text type="secondary">EMA12 / EMA26</Text><div>{formatTechnicalNumber(state.response.metrics.ema12)} / {formatTechnicalNumber(state.response.metrics.ema26)}</div></div>
              <div><Text type="secondary">RSI14</Text><div>{formatTechnicalNumber(state.response.metrics.rsi14, 1)}</div></div>
              <div><Text type="secondary">MACD / Signal / Hist</Text><div>{formatTechnicalNumber(state.response.metrics.macd)} / {formatTechnicalNumber(state.response.metrics.macdSignal)} / {formatTechnicalNumber(state.response.metrics.macdHistogram)}</div></div>
              <div><Text type="secondary">1м / 3м / 6м / 1г</Text><div>{formatPercentPoints(state.response.metrics.return1Month)} / {formatPercentPoints(state.response.metrics.return3Months)} / {formatPercentPoints(state.response.metrics.return6Months)} / {formatPercentPoints(state.response.metrics.return1Year)}</div></div>
              <div><Text type="secondary">Volatility20 / Volatility60</Text><div>{formatFractionAsPercent(state.response.metrics.volatilityAnnualized20, 1)} / {formatFractionAsPercent(state.response.metrics.volatilityAnnualized60, 1)}</div></div>
              <div><Text type="secondary">Max Drawdown</Text><div>{formatPercentPoints(state.response.metrics.maxDrawdown, 1)}</div></div>
              <div><Text type="secondary">ATR14</Text><div>{formatTechnicalNumber(state.response.metrics.atr14, 3)}</div></div>
            </div>
            {state.response.metrics.priceBasis.length > 0 && (
              <div className="technical-analysis-panel__price-basis">
                <h5>База цен</h5>
                <ul>
                  {state.response.metrics.priceBasis.map((basis, idx) => (
                    <li key={`${basis.metric}-${idx}`}>
                      <strong>{basis.metric}</strong>: {basis.basis}. {basis.reason}
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </details>

          <Text type="secondary" className="technical-analysis-panel__disclaimer">
            Аналитический сигнал отражает расчёт по историческим данным и не является персональной инвестиционной рекомендацией.
          </Text>
        </div>
      )}
    </section>
  );
};

export default StockTechnicalAnalysisPanel;
