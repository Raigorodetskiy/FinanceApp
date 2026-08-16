import { describe, expect, it } from 'vitest';
import {
  UNSUPPORTED_REFRESH_MESSAGE_FALLBACK,
  classifyRefreshError,
  classifyRefreshResult,
  getErrMsg,
} from './IndexConstituentsPanel';
import type { IndexConstituentsRefreshResponse } from '../types';

function makeAxiosError(status: number | undefined, data: unknown): unknown {
  return {
    isAxiosError: true,
    response: status == null ? undefined : { status, data },
  };
}

function makeRefreshResponse(
  overrides: Partial<IndexConstituentsRefreshResponse>,
): IndexConstituentsRefreshResponse {
  return {
    marketIndexId: 1,
    providerStatus: 'Success',
    providerMessage: null,
    added: 1,
    updated: 2,
    unchanged: 3,
    closed: 4,
    ...overrides,
  };
}

describe('classifyRefreshError', () => {
  it('returns warning with providerMessage for 422 Unsupported', () => {
    const err = makeAxiosError(
      422,
      makeRefreshResponse({
        providerStatus: 'Unsupported',
        providerMessage: 'Автоматическая загрузка недоступна',
      }),
    );

    expect(classifyRefreshError(err, 'Ошибка обновления состава')).toEqual({
      kind: 'warning',
      message: 'Автоматическая загрузка недоступна',
      shouldReload: false,
    });
  });

  it('returns warning fallback for 422 Unsupported without providerMessage', () => {
    const err = makeAxiosError(
      422,
      makeRefreshResponse({
        providerStatus: 'Unsupported',
        providerMessage: '   ',
      }),
    );

    expect(classifyRefreshError(err, 'Ошибка обновления состава')).toEqual({
      kind: 'warning',
      message: UNSUPPORTED_REFRESH_MESSAGE_FALLBACK,
      shouldReload: false,
    });
  });

  it('returns generic error fallback for malformed 422 payload', () => {
    const err = makeAxiosError(422, { providerStatus: 'Unsupported' });

    expect(classifyRefreshError(err, 'Ошибка обновления состава')).toEqual({
      kind: 'error',
      message: 'Ошибка обновления состава',
      shouldReload: false,
    });
  });

  it('returns provider error for valid 422 payload with non-Unsupported status', () => {
    const err = makeAxiosError(
      422,
      makeRefreshResponse({
        providerStatus: 'Error',
        providerMessage: 'Поставщик временно недоступен',
      }),
    );

    expect(classifyRefreshError(err, 'Ошибка обновления состава')).toEqual({
      kind: 'error',
      message: 'Поставщик временно недоступен',
      shouldReload: false,
    });
  });

  it('treats unexpected 422 Success payload as error and does not reload', () => {
    const err = makeAxiosError(
      422,
      makeRefreshResponse({
        providerStatus: 'Success',
        providerMessage: 'Неконсистентный ответ поставщика',
      }),
    );

    expect(classifyRefreshError(err, 'Ошибка обновления состава')).toEqual({
      kind: 'error',
      message: 'Неконсистентный ответ поставщика',
      shouldReload: false,
    });
  });

  it('uses caller fallback for valid 422 payload without providerMessage', () => {
    const err = makeAxiosError(
      422,
      makeRefreshResponse({
        providerStatus: 'Error',
        providerMessage: '   ',
      }),
    );

    expect(classifyRefreshError(err, 'Ошибка обновления состава')).toEqual({
      kind: 'error',
      message: 'Ошибка обновления состава',
      shouldReload: false,
    });
  });

  it('returns error for 429/500 and network failures', () => {
    const tooManyRequests = makeAxiosError(429, { message: 'слишком много запросов' });
    const serverError = makeAxiosError(500, { message: 'внутренняя ошибка' });
    const networkFailure = makeAxiosError(undefined, undefined);

    expect(classifyRefreshError(tooManyRequests, 'Ошибка обновления состава').kind).toBe('error');
    expect(classifyRefreshError(serverError, 'Ошибка обновления состава').kind).toBe('error');
    expect(classifyRefreshError(networkFailure, 'Ошибка обновления состава').kind).toBe('error');
  });
});

describe('classifyRefreshResult', () => {
  it('keeps success/partial path and requests reload', () => {
    const success = classifyRefreshResult(
      makeRefreshResponse({
        providerStatus: 'Success',
        added: 5,
        unchanged: 9,
        closed: 1,
      }),
    );
    const partial = classifyRefreshResult(
      makeRefreshResponse({
        providerStatus: 'Partial',
      }),
    );

    expect(success.kind).toBe('success');
    expect(success.shouldReload).toBe(true);
    expect(success.message).toContain('Добавлено: 5');
    expect(partial.kind).toBe('success');
    expect(partial.shouldReload).toBe(true);
  });

  it('never requests reload for Unsupported', () => {
    const unsupported = classifyRefreshResult(
      makeRefreshResponse({
        providerStatus: 'Unsupported',
        providerMessage: 'Поставщик не поддерживает автоматическую загрузку',
      }),
    );

    expect(unsupported).toEqual({
      kind: 'warning',
      message: 'Поставщик не поддерживает автоматическую загрузку',
      shouldReload: false,
    });
  });
});

describe('getErrMsg', () => {
  it('prefers providerMessage, then raw string body, then message, then fallback', () => {
    const withProviderMessage = makeAxiosError(400, {
      providerMessage: 'Сообщение поставщика',
      message: 'Общее сообщение',
    });
    const withRawString = makeAxiosError(400, 'Текст ошибки как строка');
    const withMessage = makeAxiosError(400, { message: 'Общее сообщение' });
    const withoutData = makeAxiosError(400, {});

    expect(getErrMsg(withProviderMessage, 'fallback')).toBe('Сообщение поставщика');
    expect(getErrMsg(withRawString, 'fallback')).toBe('Текст ошибки как строка');
    expect(getErrMsg(withMessage, 'fallback')).toBe('Общее сообщение');
    expect(getErrMsg(withoutData, 'fallback')).toBe('fallback');
  });
});
