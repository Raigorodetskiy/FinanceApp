import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  buildIndexConstituentHistoryJobNotice,
  runIndexConstituentHistoryRefreshJob,
} from './indexConstituentHistoryRefresh';
import type { IndexConstituentHistoryRefreshJobResponse } from '../types';

const makeJob = (
  overrides: Partial<IndexConstituentHistoryRefreshJobResponse> = {},
): IndexConstituentHistoryRefreshJobResponse => ({
  jobId: 'job-1',
  marketIndexId: 1,
  stockId: 10,
  state: 'Queued',
  reusedActiveJob: false,
  createdAtUtc: '2026-08-17T00:00:00Z',
  deletedPoints: 0,
  importedPoints: 0,
  ...overrides,
});

describe('runIndexConstituentHistoryRefreshJob', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('starts job, polls status and resolves success notice', async () => {
    const startJob = vi.fn(async () => makeJob({ state: 'Queued' }));
    const getJobStatus = vi
      .fn()
      .mockResolvedValueOnce(makeJob({ state: 'Running' }))
      .mockResolvedValueOnce(makeJob({ state: 'Succeeded' }));

    const states: Array<string | null> = [];
    const notice = await runIndexConstituentHistoryRefreshJob({
      indexId: 1,
      stockId: 10,
      ticker: 'AAPL',
      startJob,
      getJobStatus,
      pollIntervalMs: 1,
      timeoutMs: 5000,
      onStateChange: (state) => states.push(state),
    });

    expect(notice?.level).toBe('success');
    expect(notice?.refreshChart).toBe(true);
    expect(states).toEqual(['Queued', 'Running', 'Succeeded']);
    expect(getJobStatus).toHaveBeenCalledTimes(2);
  });

  it('handles reused active job and emits info callback', async () => {
    const onInfo = vi.fn();
    const startJob = vi.fn(async () => makeJob({ state: 'Running', reusedActiveJob: true, jobId: 'job-reused' }));
    const getJobStatus = vi.fn(async () => makeJob({ state: 'Succeeded', jobId: 'job-reused' }));

    const notice = await runIndexConstituentHistoryRefreshJob({
      indexId: 1,
      stockId: 10,
      ticker: 'AAPL',
      startJob,
      getJobStatus,
      pollIntervalMs: 1,
      timeoutMs: 5000,
      onInfo,
    });

    expect(onInfo).toHaveBeenCalledWith(
      'Обновление истории для AAPL уже выполняется. Подключаемся к текущей задаче.',
    );
    expect(notice?.level).toBe('success');
  });

  it('returns warning for expired/unknown job (404)', async () => {
    const startJob = vi.fn(async () => makeJob({ state: 'Running' }));
    const getJobStatus = vi.fn(async () => {
      throw {
        isAxiosError: true,
        response: { status: 404, data: 'missing' },
      };
    });

    const notice = await runIndexConstituentHistoryRefreshJob({
      indexId: 1,
      stockId: 10,
      ticker: 'AAPL',
      startJob,
      getJobStatus,
      pollIntervalMs: 1,
      timeoutMs: 5000,
    });

    expect(notice).toEqual({
      level: 'warning',
      text: 'Задача обновления истории для AAPL больше недоступна (истекла/перезапуск). Запустите снова.',
      refreshChart: false,
    });
  });

  it('returns timeout notice when polling exceeds limit', async () => {
    const startJob = vi.fn(async () => makeJob({ state: 'Queued' }));
    const getJobStatus = vi.fn(async () => makeJob({ state: 'Queued' }));

    const notice = await runIndexConstituentHistoryRefreshJob({
      indexId: 1,
      stockId: 10,
      ticker: 'AAPL',
      startJob,
      getJobStatus,
      pollIntervalMs: 2,
      timeoutMs: 1,
    });

    expect(notice?.level).toBe('error');
    expect(notice?.text).toContain('истекло');
  });

  it('returns error notice on polling network errors', async () => {
    const startJob = vi.fn(async () => makeJob({ state: 'Running' }));
    const getJobStatus = vi.fn(async () => {
      throw {
        isAxiosError: true,
        response: { status: 500, data: { message: 'временная ошибка сети' } },
      };
    });

    const notice = await runIndexConstituentHistoryRefreshJob({
      indexId: 1,
      stockId: 10,
      ticker: 'AAPL',
      startJob,
      getJobStatus,
      pollIntervalMs: 1,
      timeoutMs: 5000,
    });

    expect(notice).toEqual({
      level: 'error',
      text: 'временная ошибка сети',
      refreshChart: false,
    });
  });

  it('returns null and stops polling when aborted', async () => {
    vi.useFakeTimers();
    const startJob = vi.fn(async () => makeJob({ state: 'Queued' }));
    const getJobStatus = vi.fn(async () => makeJob({ state: 'Succeeded' }));
    const abortController = new AbortController();

    const promise = runIndexConstituentHistoryRefreshJob({
      indexId: 1,
      stockId: 10,
      ticker: 'AAPL',
      startJob,
      getJobStatus,
      pollIntervalMs: 1000,
      timeoutMs: 5000,
      signal: abortController.signal,
    });

    abortController.abort();
    await vi.runAllTimersAsync();

    await expect(promise).resolves.toBeNull();
    expect(getJobStatus).not.toHaveBeenCalled();
  });
});

describe('buildIndexConstituentHistoryJobNotice', () => {
  it('maps terminal states to expected russian user notices', () => {
    expect(buildIndexConstituentHistoryJobNotice('AAPL', 'Succeeded', makeJob({ state: 'Succeeded' })).level).toBe('success');
    expect(buildIndexConstituentHistoryJobNotice('AAPL', 'RateLimited', makeJob({ state: 'RateLimited' })).level).toBe('warning');
    expect(buildIndexConstituentHistoryJobNotice('AAPL', 'Interrupted', makeJob({ state: 'Interrupted' })).level).toBe('warning');
    expect(buildIndexConstituentHistoryJobNotice('AAPL', 'Failed', makeJob({ state: 'Failed' })).level).toBe('error');
  });
});
