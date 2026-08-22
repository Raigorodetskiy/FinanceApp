// @vitest-environment jsdom
import React from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';
import SectorsPage from './SectorsPage';

const getSectorsMock = vi.fn();
const getPortfoliosMock = vi.fn();

vi.mock('../services/api', () => ({
  archiveIndustry: vi.fn(),
  archiveSector: vi.fn(),
  createIndustry: vi.fn(),
  createSector: vi.fn(),
  deleteIndustry: vi.fn(),
  deleteSector: vi.fn(),
  getPortfolios: (...args: unknown[]) => getPortfoliosMock(...args),
  getSectors: (...args: unknown[]) => getSectorsMock(...args),
  moveIndustry: vi.fn(),
  restoreIndustry: vi.fn(),
  restoreSector: vi.fn(),
  updateIndustry: vi.fn(),
  updateSector: vi.fn(),
}));

vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    user: { username: 'tester' },
    logout: vi.fn(),
  }),
}));

vi.mock('../components/AuthenticatedShell', () => ({
  default: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

describe('SectorsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getPortfoliosMock.mockResolvedValue({ data: [] });
    getSectorsMock.mockResolvedValue([
      {
        id: 15,
        name: 'Information Technology',
        normalizedName: 'INFORMATION TECHNOLOGY',
        isArchived: false,
        sortOrder: 1,
        createdAtUtc: '2026-08-19T00:00:00Z',
        updatedAtUtc: '2026-08-19T00:00:00Z',
        industryCount: 1,
        stockCount: 7,
        industries: [],
      },
    ]);
  });

  afterEach(() => {
    cleanup();
  });

  it('renders non-zero stockCount provided by API on sectors table', async () => {
    render(
      <MemoryRouter>
        <SectorsPage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(getSectorsMock).toHaveBeenCalledWith(true));
    const sectorRow = screen.getByRole('row', { name: /Information Technology/i });
    expect(within(sectorRow).getByText('7')).toBeInTheDocument();
  });
});
