import React, { useMemo, useState } from 'react';
import { Input, Space, Table, Typography, Alert } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useAuth } from '../contexts/AuthContext';
import AuthenticatedShell from '../components/AuthenticatedShell';
import { financialMetrics, FINANCIAL_METRICS_COUNT } from '../data/financialMetrics';
import type { FinancialMetric } from '../data/financialMetrics';

const { Title, Text, Paragraph } = Typography;

const DISCLAIMER =
  'Определения, нормализация и методология расчёта показателей могут различаться у эмитентов и поставщиков данных. ' +
  'Сравнивать компании по мультипликаторам рекомендуется в рамках одной отрасли и на единой методологии. ' +
  'Данный справочник носит исключительно информационный характер и не является инвестиционной рекомендацией.';

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

const DescriptionCell: React.FC<{ metric: FinancialMetric }> = ({ metric }) => (
  <Space direction="vertical" size={4} style={{ width: '100%' }}>
    <Paragraph style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{metric.description}</Paragraph>

    {metric.unit && (
      <Text type="secondary" style={{ fontSize: 12 }}>
        Единица: {metric.unit}
      </Text>
    )}

    {metric.formula && (
      <div>
        <Text type="secondary" style={{ fontSize: 12 }}>
          Формула:
        </Text>{' '}
        <Text code style={{ fontSize: 12 }}>
          {metric.formula}
        </Text>
      </div>
    )}

    {metric.example && (
      <div>
        <Text type="secondary" style={{ fontSize: 12 }}>
          Пример:
        </Text>{' '}
        <Text style={{ fontSize: 12 }}>{metric.example}</Text>
      </div>
    )}

    {metric.interpretation && (
      <Text type="warning" style={{ fontSize: 12 }}>
        ⚠ {metric.interpretation}
      </Text>
    )}
  </Space>
);

const columns: ColumnsType<FinancialMetric> = [
  {
    title: 'Название',
    dataIndex: 'name',
    key: 'name',
    width: 220,
    render: (name: string, metric) => (
      <Space direction="vertical" size={2}>
        <Text strong>{name}</Text>
        {metric.aliases && metric.aliases.length > 0 && (
          <Text type="secondary" style={{ fontSize: 11 }}>
            {metric.aliases.slice(0, 3).join(', ')}
          </Text>
        )}
      </Space>
    ),
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
        <Alert message={DISCLAIMER} type="info" showIcon />

        <div style={{ display: 'flex', gap: 16, alignItems: 'center', flexWrap: 'wrap' }}>
          <Input.Search
            placeholder="Поиск по названию, описанию, формуле, alias..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            allowClear
            style={{ maxWidth: 480 }}
          />
          <Text type="secondary">
            {filtered.length === FINANCIAL_METRICS_COUNT
              ? `${FINANCIAL_METRICS_COUNT} показателей`
              : `${filtered.length} из ${FINANCIAL_METRICS_COUNT}`}
          </Text>
        </div>

        <Table<FinancialMetric>
          rowKey="id"
          columns={columns}
          dataSource={filtered}
          pagination={false}
          size="small"
          scroll={{ x: 600 }}
          locale={{
            emptyText: (
              <Space direction="vertical" style={{ padding: 32 }}>
                <Text type="secondary">Показатели не найдены</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>
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
