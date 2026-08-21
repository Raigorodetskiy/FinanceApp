import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Button,
  Checkbox,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Space,
  Spin,
  Table,
  Tabs,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd';
import { AreaChartOutlined, CaretDownOutlined, CaretRightOutlined, DeleteOutlined, EditOutlined, InboxOutlined, OrderedListOutlined, PlusOutlined, RollbackOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import axios from 'axios';
import { useParams } from 'react-router-dom';
import AuthenticatedShell from '../components/AuthenticatedShell';
import MarketIndexPriceChart from '../components/MarketIndexPriceChart';
import IndexConstituentsPanel from '../components/IndexConstituentsPanel';
import { useAuth } from '../contexts/AuthContext';
import {
  archiveMarketIndex,
  createMarketIndex,
  deleteMarketIndex,
  getMarketIndices,
  getPortfolios,
  restoreMarketIndex,
  updateMarketIndex,
} from '../services/api';
import type { MarketIndex, Portfolio } from '../types';
import { marketIndexSidebarKey } from '../components/AppSidebar';

const { Title, Text } = Typography;
const { TextArea } = Input;
const archiveTag = <Tag color="default">Архив</Tag>;
export const MARKET_INDICES_SELECTED_KEY = 'market-indices-manage';

function getErrMsg(err: unknown, fallback: string): string {
  if (axios.isAxiosError(err)) {
    const data = err.response?.data;
    if (typeof data === 'string' && data.trim()) {
      return data.trim();
    }
    if (
      data != null
      && typeof data === 'object'
      && 'message' in data
      && typeof data.message === 'string'
      && data.message.trim()
    ) {
      return data.message.trim();
    }
  }

  return fallback;
}

export function matchesMarketIndexSearch(index: MarketIndex, search: string): boolean {
  const query = search.trim().toLowerCase();
  if (!query) {
    return true;
  }

  return [index.code, index.name, index.countryOrRegion, index.description]
    .some((value) => (value ?? '').toLowerCase().includes(query));
}

export async function loadMarketIndicesPagePortfolios(loadPortfolios: typeof getPortfolios): Promise<Portfolio[]> {
  try {
    const response = await loadPortfolios();
    return response.data;
  } catch {
    return [];
  }
}

const MarketIndicesPage: React.FC = () => {
  const { user, logout } = useAuth();
  const { id: routeId } = useParams<{ id?: string }>();
  const activeIndexId = routeId != null ? Number(routeId) : null;

  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [marketIndices, setMarketIndices] = useState<MarketIndex[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [includeArchived, setIncludeArchived] = useState(true);
  const [search, setSearch] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [modalLoading, setModalLoading] = useState(false);
  const [editingMarketIndex, setEditingMarketIndex] = useState<MarketIndex | null>(null);
  const [expandedIndexId, setExpandedIndexId] = useState<number | null>(null);
  const [expandedTab, setExpandedTab] = useState<'chart' | 'constituents'>('chart');
  const [indexTab, setIndexTab] = useState<'chart' | 'constituents'>('chart');
  const [form] = Form.useForm();
  const [messageApi, contextHolder] = message.useMessage();

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      setMarketIndices(await getMarketIndices(true));
    } catch {
      setError('Ошибка загрузки данных');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  useEffect(() => {
    let cancelled = false;
    loadMarketIndicesPagePortfolios(getPortfolios).then((items) => {
      if (!cancelled) {
        setPortfolios(items);
      }
    });

    return () => {
      cancelled = true;
    };
  }, []);

  const filteredMarketIndices = useMemo(() =>
    marketIndices
      .filter((index) => includeArchived || !index.isArchived)
      .filter((index) => matchesMarketIndexSearch(index, search)),
  [includeArchived, marketIndices, search]);

  const openCreateModal = () => {
    setEditingMarketIndex(null);
    form.resetFields();
    const maxSortOrder = marketIndices.reduce((max, item) => Math.max(max, item.sortOrder), 0);
    form.setFieldsValue({ sortOrder: maxSortOrder + 10, showInNavigation: true });
    setModalOpen(true);
  };

  const openEditModal = (marketIndex: MarketIndex) => {
    setEditingMarketIndex(marketIndex);
    form.setFieldsValue({
      name: marketIndex.name,
      code: marketIndex.code,
      providerSymbol: marketIndex.providerSymbol ?? '',
      description: marketIndex.description,
      countryOrRegion: marketIndex.countryOrRegion,
      sortOrder: marketIndex.sortOrder,
      showInNavigation: marketIndex.showInNavigation,
    });
    setModalOpen(true);
  };

  const handleSave = async () => {
    const values = await form.validateFields();
    setModalLoading(true);

    try {
      const payload = {
        name: values.name,
        code: values.code,
        providerSymbol: (values.providerSymbol as string | undefined)?.trim() || null,
        description: values.description,
        countryOrRegion: values.countryOrRegion,
        sortOrder: values.sortOrder ?? 0,
        showInNavigation: (values.showInNavigation as boolean | undefined) ?? true,
      };

      if (editingMarketIndex) {
        await updateMarketIndex(editingMarketIndex.id, payload);
        messageApi.success('Индекс обновлён');
      } else {
        await createMarketIndex(payload);
        messageApi.success('Индекс создан');
      }

      setModalOpen(false);
      await loadData();
    } catch (err) {
      messageApi.error(getErrMsg(err, 'Ошибка сохранения индекса'));
    } finally {
      setModalLoading(false);
    }
  };

  const handleArchive = async (marketIndex: MarketIndex) => {
    try {
      await archiveMarketIndex(marketIndex.id);
      messageApi.success('Индекс архивирован');
      await loadData();
    } catch (err) {
      messageApi.error(getErrMsg(err, 'Ошибка архивирования индекса'));
    }
  };

  const handleRestore = async (marketIndex: MarketIndex) => {
    try {
      await restoreMarketIndex(marketIndex.id);
      messageApi.success('Индекс восстановлен');
      await loadData();
    } catch (err) {
      messageApi.error(getErrMsg(err, 'Ошибка восстановления индекса'));
    }
  };

  const handleDelete = async (marketIndex: MarketIndex) => {
    try {
      await deleteMarketIndex(marketIndex.id);
      messageApi.success('Индекс удалён');
      await loadData();
    } catch (err) {
      messageApi.error(getErrMsg(err, 'Невозможно удалить индекс'));
    }
  };

  const handleToggleExpand = (marketIndex: MarketIndex) => {
    setExpandedIndexId((prev) => {
      if (prev === marketIndex.id) return null;
      setExpandedTab('chart');
      return marketIndex.id;
    });
  };

  const columns: ColumnsType<MarketIndex> = [
    {
      title: 'Код',
      dataIndex: 'code',
      key: 'code',
      width: 140,
      render: (_value, marketIndex) => {
        const isExpanded = expandedIndexId === marketIndex.id;
        const panelId = `index-chart-panel-${marketIndex.id}`;
        return (
          <Space>
            <button
              type="button"
              aria-expanded={isExpanded}
              aria-controls={panelId}
              aria-label={isExpanded ? `Скрыть ${marketIndex.code}` : `Показать ${marketIndex.code}`}
              onClick={() => handleToggleExpand(marketIndex)}
              style={{
                background: 'none',
                border: 'none',
                cursor: 'pointer',
                padding: 0,
                display: 'flex',
                alignItems: 'center',
                gap: 4,
                fontWeight: 600,
                fontSize: 16,
                color: '#1677ff',
              }}
            >
              {isExpanded ? <CaretDownOutlined /> : <CaretRightOutlined />}
              {marketIndex.code}
            </button>
            {marketIndex.isArchived && archiveTag}
          </Space>
        );
      },
    },
    {
      title: 'Название',
      dataIndex: 'name',
      key: 'name',
      width: 260,
    },
    {
      title: 'Страна / регион',
      dataIndex: 'countryOrRegion',
      key: 'countryOrRegion',
      width: 180,
    },
    {
      title: 'Описание',
      dataIndex: 'description',
      key: 'description',
      render: (value: string) => (
        <div style={{ whiteSpace: 'normal', overflowWrap: 'anywhere' }}>{value || '—'}</div>
      ),
    },
    {
      title: 'Действия',
      key: 'actions',
      width: 220,
      render: (_value, marketIndex) => (
        <Space size={4}>
          <Tooltip title="Редактировать">
            <Button size="small" icon={<EditOutlined />} onClick={() => openEditModal(marketIndex)} />
          </Tooltip>
          {marketIndex.isArchived ? (
            <Tooltip title="Восстановить">
              <Button size="small" icon={<RollbackOutlined />} onClick={() => void handleRestore(marketIndex)} />
            </Tooltip>
          ) : (
            <Tooltip title="Архивировать">
              <Button size="small" icon={<InboxOutlined />} onClick={() => void handleArchive(marketIndex)} />
            </Tooltip>
          )}
          <Popconfirm
            title="Удалить индекс?"
            onConfirm={() => void handleDelete(marketIndex)}
            okText="Да"
            cancelText="Нет"
          >
            <Button size="small" icon={<DeleteOutlined />} danger />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const activeIndex = useMemo(
    () => (activeIndexId != null ? marketIndices.find((idx) => idx.id === activeIndexId) ?? null : null),
    [activeIndexId, marketIndices],
  );

  const selectedKeys = useMemo(() => {
    if (activeIndexId != null) return [marketIndexSidebarKey(activeIndexId)];
    return [MARKET_INDICES_SELECTED_KEY];
  }, [activeIndexId]);

  return (
    <>
      {contextHolder}
      <AuthenticatedShell
        portfolios={portfolios}
        selectedKeys={selectedKeys}
        onLogout={logout}
        userName={user?.username}
        activePortfolioId={undefined}
        marketIndices={marketIndices}
        headerLeft={
          <Title level={4} style={{ margin: 0 }}>
            {activeIndex != null ? `${activeIndex.code} — ${activeIndex.name}` : 'Мировые индексы'}
          </Title>
        }
        headerRight={activeIndex == null ? (
          <Space>
            <Button onClick={() => setIncludeArchived((value) => !value)} type={includeArchived ? 'default' : 'dashed'}>
              {includeArchived ? 'Скрыть архивные' : 'Показать архивные'}
            </Button>
            <Button type="primary" icon={<PlusOutlined />} onClick={openCreateModal}>
              Добавить индекс
            </Button>
          </Space>
        ) : (
          <Space>
            <Tooltip title="Редактировать">
              <Button icon={<EditOutlined />} onClick={() => openEditModal(activeIndex)} />
            </Tooltip>
            {activeIndex.isArchived ? (
              <Tooltip title="Восстановить">
                <Button icon={<RollbackOutlined />} onClick={() => void handleRestore(activeIndex)} />
              </Tooltip>
            ) : (
              <Tooltip title="Архивировать">
                <Button icon={<InboxOutlined />} onClick={() => void handleArchive(activeIndex)} />
              </Tooltip>
            )}
            <Popconfirm
              title="Удалить индекс?"
              onConfirm={() => void handleDelete(activeIndex)}
              okText="Да"
              cancelText="Нет"
            >
              <Button icon={<DeleteOutlined />} danger />
            </Popconfirm>
          </Space>
        )}
      >
        {loading ? (
          <div style={{ textAlign: 'center', padding: 48 }}>
            <Spin size="large" />
          </div>
        ) : error ? (
          <Text type="danger">{error}</Text>
        ) : activeIndex != null ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            {activeIndex.isArchived && archiveTag}
            <Tabs
              activeKey={indexTab}
              onChange={(key) => setIndexTab(key as 'chart' | 'constituents')}
              size="small"
              items={[
                {
                  key: 'chart',
                  label: <><AreaChartOutlined /> График</>,
                  children: (
                    <MarketIndexPriceChart
                      panelId={`index-chart-panel-${activeIndex.id}`}
                      indexId={activeIndex.id}
                      code={activeIndex.code}
                      name={activeIndex.name}
                      providerSymbol={activeIndex.providerSymbol}
                      isArchived={activeIndex.isArchived}
                    />
                  ),
                },
                {
                  key: 'constituents',
                  label: <><OrderedListOutlined /> Состав</>,
                  children: (
                    <IndexConstituentsPanel
                      indexId={activeIndex.id}
                      isArchived={activeIndex.isArchived}
                    />
                  ),
                },
              ]}
            />
          </div>
        ) : activeIndexId != null ? (
          <Text type="secondary">Индекс не найден</Text>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <Input.Search
              placeholder="Поиск по коду, названию, стране/региону или описанию..."
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              allowClear
              style={{ maxWidth: 520 }}
            />

            {filteredMarketIndices.length === 0 ? (
              <Text type="secondary">Нет данных</Text>
            ) : (
              <Table<MarketIndex>
                rowKey="id"
                columns={columns}
                dataSource={filteredMarketIndices}
                pagination={false}
                expandable={{
                  expandedRowKeys: expandedIndexId != null ? [expandedIndexId] : [],
                  expandIcon: () => null,
                  expandedRowRender: (record) => (
                    <Tabs
                      activeKey={expandedTab}
                      onChange={(key) => setExpandedTab(key as 'chart' | 'constituents')}
                      size="small"
                      style={{ marginTop: -4 }}
                      items={[
                        {
                          key: 'chart',
                          label: <><AreaChartOutlined /> График</>,
                          children: (
                            <MarketIndexPriceChart
                              panelId={`index-chart-panel-${record.id}`}
                              indexId={record.id}
                              code={record.code}
                              name={record.name}
                              providerSymbol={record.providerSymbol}
                              isArchived={record.isArchived}
                            />
                          ),
                        },
                        {
                          key: 'constituents',
                          label: <><OrderedListOutlined /> Состав</>,
                          children: (
                            <IndexConstituentsPanel
                              indexId={record.id}
                              isArchived={record.isArchived}
                            />
                          ),
                        },
                      ]}
                    />
                  ),
                }}
              />
            )}
          </div>
        )}
      </AuthenticatedShell>

      <Modal
        title={editingMarketIndex ? 'Редактировать индекс' : 'Добавить индекс'}
        open={modalOpen}
        onOk={() => void handleSave()}
        onCancel={() => setModalOpen(false)}
        confirmLoading={modalLoading}
        okText="Сохранить"
        cancelText="Отмена"
      >
        <Form form={form} layout="vertical">
          <Form.Item
            name="code"
            label="Код"
            rules={[{ required: true, message: 'Введите код' }, { max: 50 }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="name"
            label="Название"
            rules={[{ required: true, message: 'Введите название' }, { max: 200 }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="providerSymbol"
            label="Символ поставщика (Yahoo Finance)"
            rules={[{ max: 50 }]}
            extra="Например: ^GSPC, ^DJI, ^N225. Оставьте пустым, если символ недоступен."
          >
            <Input placeholder="^GSPC" />
          </Form.Item>
          <Form.Item name="countryOrRegion" label="Страна / регион">
            <Input />
          </Form.Item>
          <Form.Item name="description" label="Описание">
            <TextArea autoSize={{ minRows: 4, maxRows: 8 }} />
          </Form.Item>
          <Form.Item name="sortOrder" label="Порядок сортировки">
            <InputNumber min={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item
            name="showInNavigation"
            valuePropName="checked"
            extra="Отображать индекс отдельным пунктом в разделе «Мировые индексы»"
          >
            <Checkbox>Показывать в навигации</Checkbox>
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};

export default MarketIndicesPage;
