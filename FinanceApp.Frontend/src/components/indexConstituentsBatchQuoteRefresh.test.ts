import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  buildBatchQuoteJobNotice,
  buildBatchQuoteJobSummary,
  runIndexConstituentsBatchQuoteRefreshJob,
} from './indexConstituentsBatchQuoteRefresh';
import type { IndexConstituentsBatchQuoteRefreshJobResponse } from '../types';

const makeJob = (
  overrides: Partial<IndexConstituentsBatchQuoteRefreshJobResponse> = {},
): IndexConstituentsBatchQuoteRefreshJobResponse => ({
  jobId: 'q-job-1',
  marketIndexId: 1,
  state: 'Queued',
  reusedActiveJob: false,
  createdAtUtc: '2026-08-17T00:00:00Z',
  total: 0,
  processed: 0,
  succeeded: 0,
  delayed: 0,
  noEurConversion: 0,
  staleRejected: 0,
  providerFailed: 0,
  persistFailed: 0,
  rateLimited: 0,
  ...overrides,
});

describe('buildBatchQuoteJobSummary', () => {
  it('reports succeeded count', () => {
    expect(buildBatchQuoteJobSummary(makeJob({ succeeded: 10, total: 10, processed: 10 }))).toContain('обновлено: 10');
  });

  it('reports delayed count', () => {
    expect(buildBatchQuoteJobSummary(makeJob({ delayed: 3 }))).toContain('задержаны: 3');
  });

  it('reports noEurConversion count', () => {
    expect(buildBatchQuoteJobSummary(makeJob({ noEurConversion: 2 }))).toContain('нет курса EUR: 2');
  });

  it('reports staleRejected count', () => {
    expect(buildBatchQuoteJobSummary(makeJob({ staleRejected: 1 }))).toContain('устаревшие (отклонены): 1');
  });

  it('sums provider and persist failures', () => {
    expect(buildBatchQuoteJobSummary(makeJob({ providerFailed: 2, persistFailed: 1 }))).toContain('ошибок: 3');
  });

  it('reports rateLimited count', () => {
    expect(buildBatchQuoteJobSummary(makeJob({ rateLimited: 5 }))).toContain('ограничение запросов: 5');
  });

  it('returns "нет изменений" when all zero', () => {
    expect(buildBatchQuoteJobSummary(makeJob())).toBe('нет изменений');
  });
});

describe('buildBatchQuoteJobNotice', () => {
  it('returns success level when Succeeded and no issues', () => {
    const notice = buildBatchQuoteJobNotice(makeJob({ state: 'Succeeded', succeeded: 5, total: 5, processed: 5 }));
    expect(notice.level).toBe('success');
    expect(notice.text).toContain('Обновление цен завершено');
  });

  it('returns warning level when Succeeded but has delayed', () => {
    const notice = buildBatchQuoteJobNotice(makeJob({ state: 'Succeeded', succeeded: 3, delayed: 2 }));
    expect(notice.level).toBe('warning');
  });

  it('returns warning for RateLimited state', () => {
    const notice = buildBatchQuoteJobNotice(makeJob({ state: 'RateLimited', error: 'слишком много' }));
    expect(notice.level).toBe('warning');
    expect(notice.text).toContain('слишком много');
  });

  it('uses RateLimited fallback when no error', () => {
    const notice = buildBatchQuoteJobNotice(makeJob({ state: 'RateLimited' }));
    expect(notice.level).toBe('warning');
    expect(notice.text).toContain('Поставщик ограничил запросы');
  });

  it('returns warning for Interrupted state', () => {
    const notice = buildBatchQuoteJobNotice(makeJob({ state: 'Interrupted' }));
    expect(notice.level).toBe('warning');
    expect(notice.text).toContain('прервано');
  });

  it('returns error for Failed state', () => {
    const notice = buildBatchQuoteJobNotice(makeJob({ state: 'Failed' }));
    expect(notice.level).toBe('error');
  });
});

describe('runIndexConstituentsBatchQuoteRefreshJob', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('starts job, polls and returns success notice', async () => {
    const startJob = vi.fn(async () => makeJob({ state: 'Queued', total: 5 }));
    const getJobStatus = vi
      .fn()
      .mockResolvedValueOnce(makeJob({ state: 'Running', total: 5, processed: 2 }))
      .mockResolvedValueOnce(makeJob({ state: 'Succeeded', total: 5, processed: 5, succeeded: 5 }));

    const progressUpdates: Array<[number, number]> = [];
    const notice = await runIndexConstituentsBatchQuoteRefreshJob({
      indexId: 1,
      startJob,
      getJobStatus,
      pollIntervalMs: 1,
      timeoutMs: 5000,
      onProgress: (p, t) => progressUpdates.push([p, t]),
    });

    expect(notice?.level).toBe('success');
    expect(notice?.text).toContain('Обновление цен завершено');
    expect(progressUpdates.some(([p, t]) => t === 5)).toBe(true);
  });

  it('handles reused active job and shows info', async () => {
    const startJob = vi.fn(async () =>
      makeJob({ state: 'Succeeded', reusedActiveJob: true, total: 3, processed: 3, succeeded: 3 }),
    );
    const getJobStatus = vi.fn();
    const infos: string[] = [];

    const notice = await runIndexConstituentsBatchQuoteRefreshJob({
      indexId: 1,
      startJob,
      getJobStatus,
      pollIntervalMs: 1,
      timeoutMs: 5000,
      onInfo: (t) => infos.push(t),
    });

    expect(notice?.level).toBe('success');
    expect(infos.length).toBeGreaterThan(0);
    expect(getJobStatus).not.toHaveBeenCalled(); // already terminal
  });

  it('returns rate-limited notice on RateLimited terminal state', async () => {
    const startJob = vi.fn(async () =>
      makeJob({ state: 'RateLimited', rateLimited: 1, error: 'ограничен' }),
    );
    const notice = await runIndexConstituentsBatchQuoteRefreshJob({
      indexId: 1,
      startJob,
      getJobStatus: vi.fn(),
      pollIntervalMs: 1,
      timeoutMs: 5000,
    });
    expect(notice?.level).toBe('warning');
    expect(notice?.text).toContain('ограничен');
  });

  it('returns 404 notice when poll returns 404', async () => {
    const startJob = vi.fn(async () => makeJob({ state: 'Running', total: 5 }));
    const getJobStatus = vi.fn().mockRejectedValueOnce({
      isAxiosError: true,
      response: { status: 404, data: 'not found' },
    });

    const notice = await runIndexConstituentsBatchQuoteRefreshJob({
      indexId: 1,
      startJob,
      getJobStatus,
      pollIntervalMs: 1,
      timeoutMs: 5000,
    });

    expect(notice?.level).toBe('warning');
    expect(notice?.text).toContain('недоступна');
  });

  it('returns timeout notice when poll exceeds timeoutMs', async () => {
    const startJob = vi.fn(async () => makeJob({ state: 'Running', total: 10 }));
    const getJobStatus = vi.fn(async () => makeJob({ state: 'Running', total: 10 }));

    const notice = await runIndexConstituentsBatchQuoteRefreshJob({
      indexId: 1,
      startJob,
      getJobStatus,
      pollIntervalMs: 1,
      timeoutMs: 20,  // very short
    });

    expect(notice?.level).toBe('error');
    expect(notice?.text).toContain('истекло');
  });

  it('returns null when aborted', async () => {
    const abort = new AbortController();
    const startJob = vi.fn(async () => {
      abort.abort();
      return makeJob({ state: 'Running', total: 5 });
    });

    const notice = await runIndexConstituentsBatchQuoteRefreshJob({
      indexId: 1,
      startJob,
      getJobStatus: vi.fn(),
      pollIntervalMs: 1,
      timeoutMs: 5000,
      signal: abort.signal,
    });

    expect(notice).toBeNull();
  });

  it('returns error notice on network poll failure', async () => {
    const startJob = vi.fn(async () => makeJob({ state: 'Running', total: 5 }));
    const getJobStatus = vi.fn().mockRejectedValueOnce(new Error('network error'));

    const notice = await runIndexConstituentsBatchQuoteRefreshJob({
      indexId: 1,
      startJob,
      getJobStatus,
      pollIntervalMs: 1,
      timeoutMs: 5000,
    });

    expect(notice?.level).toBe('error');
  });
});
