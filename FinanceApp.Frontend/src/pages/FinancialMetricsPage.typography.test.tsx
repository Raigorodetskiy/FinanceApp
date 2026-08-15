import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { FinancialMetric } from '../data/financialMetrics';
import {
  DescriptionCell,
  FINANCIAL_METRICS_ALIASES_COLOR,
  FINANCIAL_METRICS_ALIASES_FONT_SIZE,
  FINANCIAL_METRICS_ALIASES_LINE_HEIGHT,
  FINANCIAL_METRICS_DESCRIPTION_FONT_SIZE,
  FINANCIAL_METRICS_DESCRIPTION_LINE_HEIGHT,
  FINANCIAL_METRICS_META_FONT_SIZE,
  FINANCIAL_METRICS_META_LINE_HEIGHT,
  FINANCIAL_METRICS_NAME_FONT_SIZE,
  FINANCIAL_METRICS_NAME_LINE_HEIGHT,
  FINANCIAL_METRICS_TABLE_SCROLL_X,
  financialMetricsColumns,
  formatAliasesLine,
} from './FinancialMetricsPage';

const financialMetricsPageSource = readFileSync(
  new URL('./FinancialMetricsPage.tsx', import.meta.url),
  'utf8',
);

const baseMetric: FinancialMetric = {
  id: 'debt',
  name: 'Долг',
  description: 'Краткое подробное описание показателя.',
  unit: 'валюта',
  aliases: ['Total Debt', 'Debt', 'Financial Debt'],
  formula: 'Debt = Short-Term Debt + Long-Term Debt',
  example: '10 + 20 = 30',
  interpretation: 'Рост долга может увеличивать финансовые риски.',
};

describe('FinancialMetricsPage typography and layout regressions', () => {
  it('keeps exactly two main table columns and preserves horizontal responsive scroll', () => {
    expect(financialMetricsColumns).toHaveLength(2);
    expect(financialMetricsColumns.map((c) => c.title)).toEqual(['Название', 'Описание']);
    expect(FINANCIAL_METRICS_TABLE_SCROLL_X).toBe(600);
  });

  it('renders Russian name at 17px and aliases at same size, red color, line-height 1.5', () => {
    expect(FINANCIAL_METRICS_NAME_FONT_SIZE).toBe(17);
    expect(FINANCIAL_METRICS_NAME_LINE_HEIGHT).toBe(1.5);
    expect(FINANCIAL_METRICS_ALIASES_FONT_SIZE).toBe(17);
    expect(FINANCIAL_METRICS_ALIASES_COLOR).toBe('#cf1322');
    expect(FINANCIAL_METRICS_ALIASES_LINE_HEIGHT).toBe(1.5);

    const nameCell = financialMetricsColumns[0].render?.(baseMetric.name, baseMetric, 0);
    const html = renderToStaticMarkup(<>{nameCell}</>);
    expect(html).toContain('font-size:17px');
    expect(html).toContain('color:#cf1322');
    expect(html).toContain('line-height:1.5');
  });

  it('renders aliases as a plain comma-separated line without "Также называется" and without Tag markup', () => {
    expect(formatAliasesLine(baseMetric.aliases)).toBe('Total Debt, Debt, Financial Debt');

    const nameCell = financialMetricsColumns[0].render?.(baseMetric.name, baseMetric, 0);
    const html = renderToStaticMarkup(<>{nameCell}</>);
    expect(html).toContain('Total Debt, Debt, Financial Debt');
    expect(html).not.toContain('Также называется');
    expect(html).not.toContain('ant-tag');
  });

  it('does not render an empty second line when aliases are missing or empty', () => {
    expect(formatAliasesLine(undefined)).toBeNull();
    expect(formatAliasesLine([])).toBeNull();

    const metricWithoutAliases: FinancialMetric = { ...baseMetric, aliases: [] };
    const nameCell = financialMetricsColumns[0].render?.(metricWithoutAliases.name, metricWithoutAliases, 0);
    const html = renderToStaticMarkup(<>{nameCell}</>);
    expect(html).toContain('Долг');
    expect(html).not.toContain('Total Debt');
    expect(html).not.toContain('Total Debt, Debt, Financial Debt');
    expect(html).not.toContain('Financial Debt');
  });

  it('keeps description text at 16px with readable line-height', () => {
    expect(FINANCIAL_METRICS_DESCRIPTION_FONT_SIZE).toBe(16);
    expect(FINANCIAL_METRICS_DESCRIPTION_LINE_HEIGHT).toBe(1.6);

    const html = renderToStaticMarkup(<DescriptionCell metric={baseMetric} />);
    expect(html).toContain('font-size:16px');
    expect(html).toContain('line-height:1.6');
  });

  it('renders unit, formula, example and interpretation at 15px with line-height 1.5 and formula in code block', () => {
    expect(FINANCIAL_METRICS_META_FONT_SIZE).toBe(15);
    expect(FINANCIAL_METRICS_META_LINE_HEIGHT).toBe(1.5);

    const html = renderToStaticMarkup(<DescriptionCell metric={baseMetric} />);
    expect(html).toContain('Единица: валюта');
    expect(html).toContain('Формула:');
    expect(html).toContain('Пример:');
    expect(html).toContain('⚠ Рост долга может увеличивать финансовые риски.');
    expect(html).toContain('<code');
    expect(html).toContain('font-size:15px');
    expect(html).toContain('line-height:1.5');
    expect(html).not.toContain('font-size:12px');
  });

  it('does not import or render Alert and removes disclaimer text from the page source', () => {
    expect(financialMetricsPageSource).not.toContain('Alert');
    expect(financialMetricsPageSource).not.toContain('DISCLAIMER');
    expect(financialMetricsPageSource).not.toContain('Определения, нормализация и методология расчёта показателей');
  });
});
