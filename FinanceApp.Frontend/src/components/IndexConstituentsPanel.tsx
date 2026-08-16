import React, { useCallback, useEffect, useState } from 'react';
import {
  Alert,
  Button,
  Empty,
  Input,
  Space,
  Spin,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd';
import { SearchOutlined, PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import axios from 'axios';
import { getIndexConstituents, refreshIndexConstituents, trackStock } from '../services/api';
import type { IndexConstituentDto, IndexConstituentsRefreshResponse } from '../types';

const { Text } = Typography;
export const UNSUPPORTED_REFRESH_MESSAGE_FALLBACK =
  'Автоматическая загрузка состава для этого индекса не поддерживается';

export interface IndexConstituentsPanelProps {
  indexId: number;
  isArchived: boolean;
}

function isObjectRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function getNonEmptyString(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}

export function isIndexConstituentsRefreshResponse(
  value: unknown,
): value is IndexConstituentsRefreshResponse {
  if (!isObjectRecord(value)) return false;
  if (typeof value.marketIndexId !== 'number') return false;
  if (typeof value.providerStatus !== 'string') return false;
  if (typeof value.added !== 'number') return false;
  if (typeof value.updated !== 'number') return false;
  if (typeof value.unchanged !== 'number') return false;
  if (typeof value.closed !== 'number') return false;
  if ('providerMessage' in value) {
    const providerMessage = value.providerMessage;
    if (providerMessage != null && typeof providerMessage !== 'string') return false;
  }
  return true;
}

function getProviderMessageFromBody(data: unknown): string | null {
  if (!isObjectRecord(data)) return null;
  return getNonEmptyString(data.providerMessage);
}

export function getErrMsg(err: unknown, fallback: string): string {
  if (axios.isAxiosError(err)) {
    const data = err.response?.data;
    const providerMessage = getProviderMessageFromBody(data);
    if (providerMessage) return providerMessage;
    const rawMessage = getNonEmptyString(data);
    if (rawMessage) return rawMessage;
    if (isObjectRecord(data)) {
      const message = getNonEmptyString(data.message);
      if (message) return message;
    }
  }
  return fallback;
}

export type RefreshResultNotice =
  | { kind: 'warning'; message: string; shouldReload: false }
  | { kind: 'success'; message: string; shouldReload: true }
  | { kind: 'error'; message: string; shouldReload: false };

export function classifyRefreshResult(
  response: IndexConstituentsRefreshResponse,
): RefreshResultNotice {
  if (response.providerStatus === 'Unsupported') {
    return {
      kind: 'warning',
      message: getNonEmptyString(response.providerMessage) ?? UNSUPPORTED_REFRESH_MESSAGE_FALLBACK,
      shouldReload: false,
    };
  }

  if (response.providerStatus === 'Success' || response.providerStatus === 'Partial') {
    return {
      kind: 'success',
      message: `Добавлено: ${response.added}, без изменений: ${response.unchanged}, закрыто: ${response.closed}`,
      shouldReload: true,
    };
  }

  return {
    kind: 'error',
    message: getNonEmptyString(response.providerMessage) ?? 'Ошибка загрузки от поставщика',
    shouldReload: false,
  };
}

export function classifyRefreshError(err: unknown, fallback: string): RefreshResultNotice {
  if (axios.isAxiosError(err) && err.response?.status === 422) {
    const responseData = err.response.data;
    if (isIndexConstituentsRefreshResponse(responseData)) {
      if (responseData.providerStatus === 'Unsupported') {
        return {
          kind: 'warning',
          message:
            getNonEmptyString(responseData.providerMessage) ?? UNSUPPORTED_REFRESH_MESSAGE_FALLBACK,
          shouldReload: false,
        };
      }
      return {
        kind: 'error',
        message: getNonEmptyString(responseData.providerMessage) ?? fallback,
        shouldReload: false,
      };
    }
    return { kind: 'error', message: fallback, shouldReload: false };
  }

  return { kind: 'error', message: getErrMsg(err, fallback), shouldReload: false };
}

const IndexConstituentsPanel: React.FC<IndexConstituentsPanelProps> = ({
  indexId,
  isArchived,
}) => {
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [constituents, setConstituents] = useState<IndexConstituentDto[]>([]);
  const [search, setSearch] = useState('');
  const [trackingId, setTrackingId] = useState<number | null>(null);
  const [messageApi, contextHolder] = message.useMessage();

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await getIndexConstituents(indexId);
      setConstituents(res.data.constituents);
    } catch (err) {
      setError(getErrMsg(err, 'Ошибка загрузки состава индекса'));
    } finally {
      setLoading(false);
    }
  }, [indexId]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const handleRefresh = async () => {
    setRefreshing(true);
    try {
      const res = await refreshIndexConstituents(indexId);
      const result = classifyRefreshResult(res.data);
      if (result.kind === 'warning') {
        void messageApi.warning(result.message);
      } else if (result.kind === 'success') {
        void messageApi.success(result.message);
      } else {
        void messageApi.error(result.message);
      }
      if (result.shouldReload) {
        await loadData();
      }
    } catch (err) {
      const result = classifyRefreshError(err, 'Ошибка обновления состава');
      if (result.kind === 'warning') {
        void messageApi.warning(result.message);
      } else {
        void messageApi.error(result.message);
      }
    } finally {
      setRefreshing(false);
    }
  };

  const handleTrack = async (constituent: IndexConstituentDto) => {
    setTrackingId(constituent.stockId);
    try {
      await trackStock(constituent.stockId);
      void messageApi.success(`«${constituent.name}» добавлена в отслеживаемые акции`);
      setConstituents((prev) =>
        prev.map((c) =>
          c.stockId === constituent.stockId ? { ...c, trackingStatus: 'Tracked' } : c,
        ),
      );
    } catch (err) {
      void messageApi.error(getErrMsg(err, 'Ошибка добавления в отслеживаемые'));
    } finally {
      setTrackingId(null);
    }
  };

  const filteredConstituents = constituents.filter((c) => {
    if (!search.trim()) return true;
    const q = search.trim().toLowerCase();
    return (
      c.ticker.toLowerCase().includes(q) ||
      c.name.toLowerCase().includes(q) ||
      (c.isin?.toLowerCase().includes(q) ?? false) ||
      (c.providerSymbol?.toLowerCase().includes(q) ?? false)
    );
  });

  const columns: ColumnsType<IndexConstituentDto> = [
    {
      title: 'Тикер',
      key: 'ticker',
      width: 120,
      render: (_, record) => (
        <Space direction="vertical" size={0}>
          <Text strong style={{ fontSize: 13 }}>{record.ticker}</Text>
          {record.providerSymbol && record.providerSymbol !== record.ticker && (
            <Text type="secondary" style={{ fontSize: 11 }}>{record.providerSymbol}</Text>
          )}
        </Space>
      ),
    },
    {
      title: 'Компания',
      dataIndex: 'name',
      key: 'name',
      render: (name: string, record) => (
        <Space direction="vertical" size={0}>
          <Text style={{ fontSize: 13 }}>{name}</Text>
          {record.isin && (
            <Text type="secondary" style={{ fontSize: 11 }}>{record.isin}</Text>
          )}
        </Space>
      ),
    },
    {
      title: 'Биржа',
      dataIndex: 'exchange',
      key: 'exchange',
      width: 100,
      render: (exchange: string) => <Text style={{ fontSize: 13 }}>{exchange}</Text>,
    },
    {
      title: 'Источник',
      key: 'source',
      width: 130,
      render: (_, record) => (
        <Space direction="vertical" size={0}>
          <Text style={{ fontSize: 12 }}>{record.source ?? '—'}</Text>
          {record.lastVerifiedAt && (
            <Tooltip
              title={`Проверено: ${new Date(record.lastVerifiedAt).toLocaleDateString('ru-RU')}`}
            >
              <Text type="secondary" style={{ fontSize: 11 }}>
                {new Date(record.lastVerifiedAt).toLocaleDateString('ru-RU')}
              </Text>
            </Tooltip>
          )}
        </Space>
      ),
    },
    {
      title: 'Статус',
      key: 'status',
      width: 130,
      render: (_, record) => {
        const isTracked = record.trackingStatus === 'Tracked';
        return (
          <Tag color={isTracked ? 'green' : 'default'}>
            {isTracked ? 'Отслеживается' : 'В каталоге'}
          </Tag>
        );
      },
    },
    {
      title: '',
      key: 'actions',
      width: 170,
      render: (_, record) => {
        const isTracked = record.trackingStatus === 'Tracked';
        if (isTracked) return null;
        return (
          <Button
            type="primary"
            size="small"
            icon={<PlusOutlined />}
            loading={trackingId === record.stockId}
            onClick={() => void handleTrack(record)}
          >
            Добавить в акции
          </Button>
        );
      },
    },
  ];

  return (
    <div style={{ padding: '8px 0' }}>
      {contextHolder}

      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 12, flexWrap: 'wrap' }}>
        <Input
          placeholder="Поиск по тикеру, названию, ISIN…"
          prefix={<SearchOutlined />}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          allowClear
          style={{ maxWidth: 320 }}
          size="small"
        />
        {!isArchived && (
          <Button
            size="small"
            icon={<ReloadOutlined />}
            loading={refreshing}
            onClick={() => void handleRefresh()}
          >
            Обновить состав
          </Button>
        )}
      </div>

      {loading ? (
        <div style={{ padding: '24px 0', textAlign: 'center' }}>
          <Spin />
        </div>
      ) : error ? (
        <Alert type="error" message={error} showIcon />
      ) : constituents.length === 0 ? (
        <Empty
          description="Состав этого индекса не загружен. Нажмите «Обновить состав» для импорта."
          image={Empty.PRESENTED_IMAGE_SIMPLE}
        />
      ) : (
        <Table<IndexConstituentDto>
          rowKey="stockId"
          columns={columns}
          dataSource={filteredConstituents}
          size="small"
          pagination={{ pageSize: 20, showSizeChanger: false, hideOnSinglePage: true }}
          locale={{ emptyText: 'Нет компонентов, соответствующих поиску' }}
        />
      )}
    </div>
  );
};

export default IndexConstituentsPanel;
