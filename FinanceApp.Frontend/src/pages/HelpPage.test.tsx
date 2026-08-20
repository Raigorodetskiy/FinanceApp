// @vitest-environment jsdom
import React from 'react';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import HelpPage from './HelpPage';

const getPortfoliosMock = vi.fn();

vi.mock('../services/api', () => ({
  getPortfolios: (...args: unknown[]) => getPortfoliosMock(...args),
}));

vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    user: { username: 'tester' },
    logout: vi.fn(),
  }),
}));

vi.mock('../components/AuthenticatedShell', () => ({
  default: ({ children, selectedKeys }: { children: React.ReactNode; selectedKeys: string[] }) => (
    <div data-testid="shell" data-selected-keys={selectedKeys.join(',')}>
      {children}
    </div>
  ),
}));

const renderHelp = (entry = '/help') => render(
  <MemoryRouter initialEntries={[entry]}>
    <Routes>
      <Route path="/help/:articleSlug" element={<HelpPage />} />
      <Route path="/help" element={<HelpPage />} />
    </Routes>
  </MemoryRouter>,
);

describe('HelpPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getPortfoliosMock.mockResolvedValue({ data: [{ id: 1, name: 'Main' }] });

    Object.defineProperty(window, 'navigator', {
      value: {
        ...window.navigator,
        clipboard: { writeText: vi.fn().mockResolvedValue(undefined) },
      },
      configurable: true,
    });
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('renders in authenticated shell and selects help in sidebar', async () => {
    renderHelp('/help');
    expect(await screen.findByTestId('shell')).toHaveAttribute('data-selected-keys', 'help');
    expect(getPortfoliosMock).toHaveBeenCalledTimes(1);
  });

  it('shows default landing state and opens article from navigation', async () => {
    const user = userEvent.setup();
    renderHelp('/help');

    expect((await screen.findAllByText('Центр справки FinanceApp')).length).toBeGreaterThan(0);
    await user.click(screen.getByRole('link', { name: 'Аналитический сигнал: как читать' }));
    expect(await screen.findByRole('heading', { name: 'Аналитический сигнал: как читать' })).toBeInTheDocument();
  });

  it('supports direct deep link to article and section hash', async () => {
    renderHelp('/help/analytical-signal#signal-components-weights');
    expect(await screen.findByRole('heading', { name: 'Компоненты и точные веса по горизонтам' })).toBeInTheDocument();
  });

  it('supports direct deep links to RSI14 and MACD sections', async () => {
    renderHelp('/help/technical-indicators#indicators-rsi14-calculation');
    expect(await screen.findByRole('heading', { name: 'RSI14: формула, требования к данным и правила Momentum в FinanceApp' })).toBeInTheDocument();

    cleanup();
    renderHelp('/help/technical-indicators#indicators-macd-calculation');
    expect(await screen.findByRole('heading', { name: 'MACD 12/26/9: формула, seed и правила Momentum в FinanceApp' })).toBeInTheDocument();
  });

  it('handles malformed unknown article slug safely', async () => {
    renderHelp('/help/unknown-article');
    expect(await screen.findByText('Статья не найдена')).toBeInTheDocument();
    expect(screen.getByText('Выберите статью слева или начните с быстрого старта.')).toBeInTheDocument();
  });

  it('searches with normalization and shows no-results state', async () => {
    const user = userEvent.setup();
    renderHelp('/help');

    const search = await screen.findByRole('searchbox', { name: 'Поиск по справке' });
    await user.type(search, '   АНАЛИТИЧЕСКИЙ    СИГНАЛ ');
    await user.keyboard('{Enter}');

    expect(await screen.findByRole('heading', { name: 'Аналитический сигнал: как читать' })).toBeInTheDocument();

    await user.clear(search);
    await user.type(search, 'несуществующий-термин-12345');
    expect(await screen.findByText('Ничего не найдено. Попробуйте сократить запрос или использовать ключевые слова из заголовков.')).toBeInTheDocument();
  });

  it('copies article link and reports clipboard failure accessibly', async () => {
    const user = userEvent.setup();
    renderHelp('/help');

    await user.click(await screen.findByRole('link', { name: 'Аналитический сигнал: как читать' }));
    await screen.findByRole('heading', { name: 'Аналитический сигнал: как читать' });

    const copyLinkText = await screen.findByText('Копировать ссылку');
    await user.click(copyLinkText.closest('button') ?? copyLinkText);
    await waitFor(() => {
      expect(screen.getByText('Ссылка скопирована.')).toBeInTheDocument();
    });

    const writeText = vi.fn().mockRejectedValue(new Error('denied'));
    Object.defineProperty(window, 'navigator', {
      value: { ...window.navigator, clipboard: { writeText } },
      configurable: true,
    });

    const copyLinkTextRetry = await screen.findByText('Копировать ссылку');
    await user.click(copyLinkTextRetry.closest('button') ?? copyLinkTextRetry);
    await waitFor(() => {
      expect(screen.getByText('Не удалось скопировать ссылку. Скопируйте адрес вручную.')).toBeInTheDocument();
    });
  });
});
