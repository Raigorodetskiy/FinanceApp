import axios from 'axios';
import type {
  IndexConstituentHistoryRefreshJobResponse,
  IndexConstituentHistoryRefreshJobState,
} from '../types';

export const INDEX_HISTORY_JOB_POLL_INTERVAL_MS = 2500;
export const INDEX_HISTORY_JOB_POLL_TIMEOUT_MS = 4 * 60 * 1000;

export type HistoryJobNotice = {
  level: 'success' | 'warning' | 'error' | 'info';
  text: string;
  refreshChart: boolean;
};

export type HistoryJobStateCallback = (state: IndexConstituentHistoryRefreshJobState | null) => void;

export interface RunIndexConstituentHistoryRefreshOptions {
  indexId: number;
  stockId: number;
  ticker: string;
  startJob: (indexId: number, stockId: number) => Promise<IndexConstituentHistoryRefreshJobResponse>;
  getJobStatus: (
    indexId: number,
    stockId: number,
    jobId: string,
  ) => Promise<IndexConstituentHistoryRefreshJobResponse>;
  onInfo?: (text: string) => void;
  onStateChange?: HistoryJobStateCallback;
  pollIntervalMs?: number;
  timeoutMs?: number;
  signal?: AbortSignal;
}

const isRunningState = (state: IndexConstituentHistoryRefreshJobState): boolean =>
  state === 'Queued' || state === 'Running';

export function buildIndexConstituentHistoryJobNotice(
  ticker: string,
  state: IndexConstituentHistoryRefreshJobState,
  payload: IndexConstituentHistoryRefreshJobResponse,
): HistoryJobNotice {
  const providerError = getNonEmptyString(payload.error);
  if (state === 'Succeeded') {
    return { level: 'success', text: `Исторические данные обновлены для ${ticker}`, refreshChart: true };
  }
  if (state === 'RateLimited') {
    return {
      level: 'warning',
      text: providerError ?? `Поставщик ограничил запросы для ${ticker}. Попробуйте позже.`,
      refreshChart: false,
    };
  }
  if (state === 'Interrupted') {
    return {
      level: 'warning',
      text: providerError ?? `Обновление истории для ${ticker} прервано. Повторите попытку.`,
      refreshChart: false,
    };
  }
  return {
    level: 'error',
    text: providerError ?? `Ошибка обновления исторических данных для ${ticker}`,
    refreshChart: false,
  };
}

export function getHistoryRefreshErrorMessage(err: unknown, fallback: string): string {
  if (axios.isAxiosError(err)) {
    const data = err.response?.data;
    if (typeof data === 'string' && data.trim()) {
      return data;
    }
    if (data != null && typeof data === 'object' && 'message' in data && typeof data.message === 'string' && data.message.trim()) {
      return data.message;
    }
  }

  return fallback;
}

export async function runIndexConstituentHistoryRefreshJob(
  options: RunIndexConstituentHistoryRefreshOptions,
): Promise<HistoryJobNotice | null> {
  const {
    indexId,
    stockId,
    ticker,
    startJob,
    getJobStatus,
    onInfo,
    onStateChange,
    pollIntervalMs = INDEX_HISTORY_JOB_POLL_INTERVAL_MS,
    timeoutMs = INDEX_HISTORY_JOB_POLL_TIMEOUT_MS,
    signal,
  } = options;

  const initial = await startJob(indexId, stockId);
  if (signal?.aborted) {
    return null;
  }

  if (initial.reusedActiveJob) {
    onInfo?.(`Обновление истории для ${ticker} уже выполняется. Подключаемся к текущей задаче.`);
  }

  onStateChange?.(initial.state);
  if (!isRunningState(initial.state)) {
    return buildIndexConstituentHistoryJobNotice(ticker, initial.state, initial);
  }

  const startedAt = Date.now();

  while (!signal?.aborted) {
    if (Date.now() - startedAt > timeoutMs) {
      return {
        level: 'error',
        text: `Время ожидания обновления истории для ${ticker} истекло. Проверьте позже.`,
        refreshChart: false,
      };
    }

    try {
      await waitForDelay(pollIntervalMs, signal);
    } catch {
      return null;
    }

    if (signal?.aborted) {
      return null;
    }

    try {
      const payload = await getJobStatus(indexId, stockId, initial.jobId);
      onStateChange?.(payload.state);
      if (isRunningState(payload.state)) {
        continue;
      }

      return buildIndexConstituentHistoryJobNotice(ticker, payload.state, payload);
    } catch (err) {
      if (axios.isAxiosError(err) && err.response?.status === 404) {
        return {
          level: 'warning',
          text: `Задача обновления истории для ${ticker} больше недоступна (истекла/перезапуск). Запустите снова.`,
          refreshChart: false,
        };
      }

      return {
        level: 'error',
        text: getHistoryRefreshErrorMessage(err, `Ошибка проверки статуса обновления истории для ${ticker}`),
        refreshChart: false,
      };
    }
  }

  return null;
}

function waitForDelay(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(new Error('aborted'));
      return;
    }

    const timer = setTimeout(() => {
      cleanup();
      resolve();
    }, ms);

    const onAbort = () => {
      clearTimeout(timer);
      cleanup();
      reject(new Error('aborted'));
    };

    const cleanup = () => {
      signal?.removeEventListener('abort', onAbort);
    };

    if (signal) {
      signal.addEventListener('abort', onAbort, { once: true });
    }
  });
}

function getNonEmptyString(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}
