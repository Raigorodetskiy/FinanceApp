import React, { useMemo, useState } from 'react';
import { Input, Space, Table, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useAuth } from '../contexts/AuthContext';
import AuthenticatedShell from '../components/AuthenticatedShell';
import { financialMetrics, FINANCIAL_METRICS_COUNT } from '../data/financialMetrics';
import type { FinancialMetric } from '../data/financialMetrics';

const { Title, Text, Paragraph } = Typography;

export const FINANCIAL_METRICS_NAME_FONT_SIZE = 17;
export const FINANCIAL_METRICS_NAME_LINE_HEIGHT = 1.5;
export const FINANCIAL_METRICS_ALIASES_FONT_SIZE = 17;
export const FINANCIAL_METRICS_ALIASES_COLOR = '#cf1322';
export const FINANCIAL_METRICS_ALIASES_LINE_HEIGHT = 1.5;
export const FINANCIAL_METRICS_DESCRIPTION_FONT_SIZE = 16;
export const FINANCIAL_METRICS_DESCRIPTION_LINE_HEIGHT = 1.6;
export const FINANCIAL_METRICS_META_FONT_SIZE = 15;
export const FINANCIAL_METRICS_META_LINE_HEIGHT = 1.5;
export const FINANCIAL_METRICS_TABLE_SCROLL_X = 600;

/** Sorted baseline — alphabetically by name (Russian locale) */
const SORTED_METRICS: FinancialMetric[] = [...financialMetrics].sort((a, b) =>
  a.name.localeCompare(b.name, 'ru'),
);

function matchesQuery(metric: FinancialMetric, query: string): boolean {
  const q = query.toLowerCase().trim();
  if (!q) return true;
  if (metric.name.toLowerCase().includes(q)) return true;
  if (metric.description.toLowerCase().includes(q)) return true;
  if (metric.formula && metric.formula.toLowerCase().includes(q)) return true;
  if (metric.aliases && metric.aliases.some((a) => a.toLowerCase().includes(q))) return true;
  return false;
}

export function formatAliasesLine(aliases?: string[]): string | null {
  if (!aliases || aliases.length === 0) return null;
  return aliases.join(', ');
}

export const DescriptionCell: React.FC<{ metric: FinancialMetric }> = ({ metric }) => (
  <Space direction="vertical" size={4} style={{ width: '100%' }}>
    <Paragraph
      style={{
        margin: 0,
        whiteSpace: 'pre-wrap',
        fontSize: FINANCIAL_METRICS_DESCRIPTION_FONT_SIZE,
        lineHeight: FINANCIAL_METRICS_DESCRIPTION_LINE_HEIGHT,
      }}
    >
      {metric.description}
    </Paragraph>

    {metric.unit && (
      <Text
        type="secondary"
        style={{ fontSize: FINANCIAL_METRICS_META_FONT_SIZE, lineHeight: FINANCIAL_METRICS_META_LINE_HEIGHT }}
      >
        Единица: {metric.unit}
      </Text>
    )}

    {metric.formula && (
      <div>
        <Text
          type="secondary"
          style={{ fontSize: FINANCIAL_METRICS_META_FONT_SIZE, lineHeight: FINANCIAL_METRICS_META_LINE_HEIGHT }}
        >
          Формула:
        </Text>
        <div>
          <Text
            code
            style={{
              fontSize: FINANCIAL_METRICS_META_FONT_SIZE,
              lineHeight: FINANCIAL_METRICS_META_LINE_HEIGHT,
              whiteSpace: 'pre-wrap',
              display: 'inline-block',
            }}
          >
            {metric.formula}
          </Text>
        </div>
      </div>
    )}

    {metric.example && (
      <div>
        <Text
          type="secondary"
          style={{ fontSize: FINANCIAL_METRICS_META_FONT_SIZE, lineHeight: FINANCIAL_METRICS_META_LINE_HEIGHT }}
        >
          Пример:
        </Text>{' '}
        <Text style={{ fontSize: FINANCIAL_METRICS_META_FONT_SIZE, lineHeight: FINANCIAL_METRICS_META_LINE_HEIGHT }}>
          {metric.example}
        </Text>
      </div>
    )}

    {metric.interpretation && (
      <Text
        type="warning"
        style={{ fontSize: FINANCIAL_METRICS_META_FONT_SIZE, lineHeight: FINANCIAL_METRICS_META_LINE_HEIGHT }}
      >
        ⚠ {metric.interpretation}
      </Text>
    )}
  </Space>
);

export const financialMetricsColumns: ColumnsType<FinancialMetric> = [
  {
    title: 'Название',
    dataIndex: 'name',
    key: 'name',
    width: 220,
    render: (name: string, metric) => {
      const aliasesLine = formatAliasesLine(metric.aliases);
      return (
        <Space direction="vertical" size={2} style={{ width: '100%' }}>
          <Text
            strong
            style={{ fontSize: FINANCIAL_METRICS_NAME_FONT_SIZE, lineHeight: FINANCIAL_METRICS_NAME_LINE_HEIGHT }}
          >
            {name}
          </Text>
          {aliasesLine && (
            <Text
              style={{
                fontSize: FINANCIAL_METRICS_ALIASES_FONT_SIZE,
                color: FINANCIAL_METRICS_ALIASES_COLOR,
                lineHeight: FINANCIAL_METRICS_ALIASES_LINE_HEIGHT,
                whiteSpace: 'normal',
                overflowWrap: 'anywhere',
              }}
            >
              {aliasesLine}
            </Text>
          )}
        </Space>
      );
    },
  },
  {
    title: 'Описание',
    key: 'description',
    render: (_value, metric) => <DescriptionCell metric={metric} />,
  },
];

const FinancialMetricsPage: React.FC = () => {
  const { user, logout } = useAuth();
  const [search, setSearch] = useState('');

  const filtered = useMemo(() => {
    return SORTED_METRICS.filter((m) => matchesQuery(m, search));
  }, [search]);

  return (
    <AuthenticatedShell
      portfolios={[]}
      selectedKeys={['financial-metrics']}
      onLogout={logout}
      userName={user?.username}
      activePortfolioId={undefined}
      headerLeft={<Title level={4} style={{ margin: 0 }}>Финансовые показатели</Title>}
    >
      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        <div style={{ display: 'flex', gap: 16, alignItems: 'center', flexWrap: 'wrap' }}>
          <Input.Search
            placeholder="Поиск по названию, описанию, формуле, alias..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            allowClear
            style={{ maxWidth: 480, fontSize: FINANCIAL_METRICS_META_FONT_SIZE }}
          />
          <Text
            type="secondary"
            style={{ fontSize: FINANCIAL_METRICS_META_FONT_SIZE, lineHeight: FINANCIAL_METRICS_META_LINE_HEIGHT }}
          >
            {filtered.length === FINANCIAL_METRICS_COUNT
              ? `${FINANCIAL_METRICS_COUNT} показателей`
              : `${filtered.length} из ${FINANCIAL_METRICS_COUNT}`}
          </Text>
        </div>

        <Table<FinancialMetric>
          rowKey="id"
          columns={financialMetricsColumns}
          dataSource={filtered}
          pagination={false}
          size="small"
          scroll={{ x: FINANCIAL_METRICS_TABLE_SCROLL_X }}
          locale={{
            emptyText: (
              <Space direction="vertical" style={{ padding: 32 }}>
                <Text
                  type="secondary"
                  style={{ fontSize: FINANCIAL_METRICS_META_FONT_SIZE, lineHeight: FINANCIAL_METRICS_META_LINE_HEIGHT }}
                >
                  Показатели не найдены
                </Text>
                <Text
                  type="secondary"
                  style={{ fontSize: FINANCIAL_METRICS_META_FONT_SIZE, lineHeight: FINANCIAL_METRICS_META_LINE_HEIGHT }}
                >
                  Попробуйте изменить поисковый запрос
                </Text>
              </Space>
            ),
          }}
        />
      </Space>
    </AuthenticatedShell>
  );
};

export default FinancialMetricsPage;
