import { beforeEach, describe, expect, it, vi } from 'vitest';
import api, { getSectors } from './api';

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

describe('getSectors response normalization', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('keeps non-zero stock counts from camelCase API payload', async () => {
    vi.spyOn(api, 'get').mockResolvedValueOnce({
      data: [
        {
          id: 1,
          name: 'Information Technology',
          normalizedName: 'INFORMATION TECHNOLOGY',
          isArchived: false,
          sortOrder: 1,
          createdAtUtc: '2026-08-19T00:00:00Z',
          updatedAtUtc: '2026-08-19T00:00:00Z',
          industryCount: 1,
          stockCount: 95,
          industries: [
            {
              id: 11,
              sectorId: 1,
              name: 'Software',
              normalizedName: 'SOFTWARE',
              isArchived: false,
              sortOrder: 1,
              createdAtUtc: '2026-08-19T00:00:00Z',
              updatedAtUtc: '2026-08-19T00:00:00Z',
              stockCount: 27,
            },
          ],
        },
      ],
    } as never);

    const result = await getSectors(true);
    expect(result[0]?.stockCount).toBe(95);
    expect(result[0]?.industries[0]?.stockCount).toBe(27);
  });

  it('reads non-zero stock counts from PascalCase payload fields', async () => {
    vi.spyOn(api, 'get').mockResolvedValueOnce({
      data: [
        {
          id: 2,
          name: 'Industrials',
          normalizedName: 'INDUSTRIALS',
          isArchived: false,
          sortOrder: 2,
          createdAtUtc: '2026-08-19T00:00:00Z',
          updatedAtUtc: '2026-08-19T00:00:00Z',
          IndustryCount: 1,
          StockCount: 83,
          Industries: [
            {
              id: 12,
              sectorId: 2,
              name: 'Aerospace',
              normalizedName: 'AEROSPACE',
              isArchived: false,
              sortOrder: 1,
              createdAtUtc: '2026-08-19T00:00:00Z',
              updatedAtUtc: '2026-08-19T00:00:00Z',
              StockCount: 14,
            },
          ],
        },
      ],
    } as never);

    const result = await getSectors(false);
    expect(result[0]?.stockCount).toBe(83);
    expect(result[0]?.industryCount).toBe(1);
    expect(result[0]?.industries[0]?.stockCount).toBe(14);
  });
});
