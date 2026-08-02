import { beforeEach, describe, expect, it, vi } from 'vitest';
import api from './api';

describe('api response interceptor', () => {
  const removeItem = vi.fn();

  beforeEach(() => {
    removeItem.mockReset();
    vi.stubGlobal('localStorage', {
      getItem: vi.fn(() => null),
      removeItem,
    });
    vi.stubGlobal('window', {
      location: {
        href: '',
      },
    });
  });

  it('removes token and redirects to /financeapp/login on 401', async () => {
    const rejected = (api.interceptors.response as any).handlers[0].rejected as (error: unknown) => Promise<never>;
    const error = { response: { status: 401 } };

    await expect(rejected(error)).rejects.toBe(error);
    expect(removeItem).toHaveBeenCalledWith('token');
    expect(window.location.href).toBe('/financeapp/login');
  });
});
