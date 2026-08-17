import axios from 'axios';
import type {
  IndexConstituentsBatchQuoteRefreshJobResponse,
  IndexConstituentsBatchQuoteRefreshJobState,
} from '../types';

export const INDEX_BATCH_QUOTE_JOB_POLL_INTERVAL_MS = 2500;
export const INDEX_BATCH_QUOTE_JOB_POLL_TIMEOUT_MS = 10 * 60 * 1000;

export type BatchQuoteJobNotice = {
  level: 'success' | 'warning' | 'error' | 'info';
  text: string;
};

export type BatchQuoteProgressCallback = (processed: number, total: number) => void;
export type BatchQuoteRetryWaitCallback = (text: string | null) => void;

export interface RunIndexConstituentsBatchQuoteRefreshOptions {
  indexId: number;
  startJob: (indexId: number) => Promise<IndexConstituentsBatchQuoteRefreshJobResponse>;
  getJobStatus: (indexId: number, jobId: string) => Promise<IndexConstituentsBatchQuoteRefreshJobResponse>;
  onProgress?: BatchQuoteProgressCallback;
  onInfo?: (text: string) => void;
  onRetryWaitText?: BatchQuoteRetryWaitCallback;
  pollIntervalMs?: number;
  timeoutMs?: number;
  signal?: AbortSignal;
}

const isRunningState = (state: IndexConstituentsBatchQuoteRefreshJobState): boolean =>
  state === 'Queued' || state === 'Running';

const isKnownState = (state: unknown): state is IndexConstituentsBatchQuoteRefreshJobState =>
  state === 'Queued'
  || state === 'Running'
  || state === 'Succeeded'
  || state === 'RateLimited'
  || state === 'Failed'
  || state === 'Interrupted';

function buildInvalidStateNotice(rawState: unknown): BatchQuoteJobNotice {
  const rendered = typeof rawState === 'string'
    ? rawState
    : rawState == null
      ? 'пустое значение'
      : String(rawState);
  return {
    level: 'error',
    text: `Сервер вернул некорректный статус задачи обновления цен (${rendered}). Проверьте совместимость версий frontend и backend.`,
  };
}

export function buildBatchQuoteJobSummary(
  payload: IndexConstituentsBatchQuoteRefreshJobResponse,
): string {
  const parts: string[] = [];
  if (payload.succeeded > 0) parts.push(`обновлено: ${payload.succeeded}`);
  if (payload.delayed > 0) parts.push(`задержаны: ${payload.delayed}`);
  if (payload.noEurConversion > 0) parts.push(`нет курса EUR: ${payload.noEurConversion}`);
  if (payload.staleRejected > 0) parts.push(`устаревшие (отклонены): ${payload.staleRejected}`);
  const failed = (payload.providerFailed ?? 0) + (payload.persistFailed ?? 0);
  if (failed > 0) parts.push(`ошибок: ${failed}`);
  if (payload.rateLimited > 0) parts.push(`ограничение запросов: ${payload.rateLimited}`);
  if (payload.rateLimitRetries > 0) parts.push(`повторов после лимита: ${payload.rateLimitRetries}`);
  if (payload.rateLimitedSkipped > 0) parts.push(`пропущено из-за лимита: ${payload.rateLimitedSkipped}`);
  return parts.length > 0 ? parts.join(', ') : 'нет изменений';
}

export function buildBatchQuoteJobNotice(
  payload: IndexConstituentsBatchQuoteRefreshJobResponse,
): BatchQuoteJobNotice {
  const state = payload.state;
  const summary = buildBatchQuoteJobSummary(payload);
  const providerError = getNonEmptyString(payload.error);

  if (state === 'Succeeded') {
    const hasAnyIssues = payload.delayed > 0 || payload.noEurConversion > 0 ||
      (payload.providerFailed ?? 0) > 0 || (payload.persistFailed ?? 0) > 0 || payload.staleRejected > 0;
    return {
      level: hasAnyIssues ? 'warning' : 'success',
      text: `Обновление цен завершено. ${summary}.`,
    };
  }
  if (state === 'RateLimited') {
    return {
      level: 'warning',
      text: providerError ?? `Поставщик ограничил запросы. ${summary}.`,
    };
  }
  if (state === 'Interrupted') {
    return {
      level: 'warning',
      text: providerError ?? `Обновление цен прервано. ${summary}.`,
    };
  }
  return {
    level: 'error',
    text: providerError ?? `Ошибка обновления цен. ${summary}.`,
  };
}

export function getBatchQuoteErrorMessage(err: unknown, fallback: string): string {
  if (axios.isAxiosError(err)) {
    const data = err.response?.data;
    if (typeof data === 'string' && data.trim()) return data;
    if (data != null && typeof data === 'object' && 'message' in data &&
      typeof data.message === 'string' && data.message.trim()) {
      return data.message;
    }
  }
  return fallback;
}

export async function runIndexConstituentsBatchQuoteRefreshJob(
  options: RunIndexConstituentsBatchQuoteRefreshOptions,
): Promise<BatchQuoteJobNotice | null> {
  const {
    indexId,
    startJob,
    getJobStatus,
    onProgress,
    onInfo,
    onRetryWaitText,
    pollIntervalMs = INDEX_BATCH_QUOTE_JOB_POLL_INTERVAL_MS,
    timeoutMs = INDEX_BATCH_QUOTE_JOB_POLL_TIMEOUT_MS,
    signal,
  } = options;

  const initial = await startJob(indexId);
  if (signal?.aborted) return null;

  if (initial.reusedActiveJob) {
    onInfo?.('Обновление цен уже выполняется. Подключаемся к текущей задаче.');
  }

  if (initial.total > 0) {
    onProgress?.(initial.processed, initial.total);
  }
  onRetryWaitText?.(formatRetryWaitText(initial));

  if (!isKnownState(initial.state)) {
    return buildInvalidStateNotice(initial.state);
  }

  if (!isRunningState(initial.state)) {
    return buildBatchQuoteJobNotice(initial);
  }

  const startedAt = Date.now();

  while (!signal?.aborted) {
    if (Date.now() - startedAt > timeoutMs) {
      return {
        level: 'error',
        text: 'Время ожидания обновления цен истекло. Проверьте позже.',
      };
    }

    try {
      await waitForDelay(pollIntervalMs, signal);
    } catch {
      return null;
    }

    if (signal?.aborted) return null;

    try {
      const payload = await getJobStatus(indexId, initial.jobId);
      if (payload.total > 0) {
        onProgress?.(payload.processed, payload.total);
      }
      onRetryWaitText?.(formatRetryWaitText(payload));
      if (!isKnownState(payload.state)) {
        return buildInvalidStateNotice(payload.state);
      }
      if (isRunningState(payload.state)) continue;
      return buildBatchQuoteJobNotice(payload);
    } catch (err) {
      if (axios.isAxiosError(err) && err.response?.status === 404) {
        return {
          level: 'warning',
          text: 'Задача обновления цен больше недоступна (истекла/перезапуск). Запустите снова.',
        };
      }
      return {
        level: 'error',
        text: getBatchQuoteErrorMessage(err, 'Ошибка проверки статуса обновления цен'),
      };
    }
  }

  return null;
}

function formatRetryWaitText(payload: IndexConstituentsBatchQuoteRefreshJobResponse): string | null {
  if (!payload.isWaitingForRetry || !payload.nextRetryAtUtc) {
    return null;
  }

  const nextRetryAt = new Date(payload.nextRetryAtUtc).getTime();
  if (!Number.isFinite(nextRetryAt)) {
    return 'Поставщик ограничил запросы, ожидаем повтор.';
  }

  const remainingMs = nextRetryAt - Date.now();
  const remainingSeconds = Math.max(1, Math.ceil(remainingMs / 1000));
  return `Поставщик ограничил запросы, повтор через ${remainingSeconds} с.`;
}

function waitForDelay(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) { reject(new Error('aborted')); return; }
    const timer = setTimeout(() => { cleanup(); resolve(); }, ms);
    const onAbort = () => { clearTimeout(timer); cleanup(); reject(new Error('aborted')); };
    const cleanup = () => { signal?.removeEventListener('abort', onAbort); };
    if (signal) signal.addEventListener('abort', onAbort, { once: true });
  });
}

function getNonEmptyString(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const t = value.trim();
  return t ? t : null;
}
