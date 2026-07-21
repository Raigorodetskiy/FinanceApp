import React, { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import {
  Layout,
  Menu,
  Table,
  Button,
  Card,
  Modal,
  Form,
  Input,
  InputNumber,
  Segmented,
  Spin,
  Typography,
  Popconfirm,
  message,
  Tag,
  Select,
  Empty,
} from 'antd';
import {
  DashboardOutlined,
  FolderOutlined,
  StockOutlined,
  UserOutlined,
  LogoutOutlined,
  PlusOutlined,
  EditOutlined,
  DeleteOutlined,
  ReloadOutlined,
} from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import dayjs from 'dayjs';
import {
  getStocks,
  createStock,
  updateStock,
  deleteStock,
  getPortfolios,
  getStockPrice,
  getEurUsdRate,
  getStockHistory,
} from '../services/api';
import { useAuth } from '../contexts/AuthContext';
import type { Stock, Portfolio, StockHistoryPoint, StockHistoryRange } from '../types';
import { LineChart, Line, CartesianGrid, XAxis, YAxis, Tooltip, ResponsiveContainer } from 'recharts';

const { Sider, Header, Content } = Layout;
const { Title, Text } = Typography;

const AUTO_REFRESH_INTERVAL = 10 * 60; // 10 minutes in seconds

const marketStateLabel: Record<string, { color: string; text: string }> = {
  REGULAR: { color: 'green', text: 'Open' },
  PRE:     { color: 'blue',  text: 'Pre-Market' },
  POST:    { color: 'orange', text: 'After-Hours' },
  CLOSED:  { color: 'default', text: 'Closed' },
};

const StocksPage: React.FC = () => {
  const [stocks, setStocks] = useState<Stock[]>([]);
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingStock, setEditingStock] = useState<Stock | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [livePrices, setLivePrices] = useState<Record<number, { price: number | null; priceEur: number | null; loading: boolean; marketState?: string }>>({});
  const [selectedStockId, setSelectedStockId] = useState<number | null>(null);
  const [historyRange, setHistoryRange] = useState<StockHistoryRange>('1y');
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyData, setHistoryData] = useState<StockHistoryPoint[]>([]);
  const [historyEurUsdRate, setHistoryEurUsdRate] = useState<number | null>(null);
  const [countdown, setCountdown] = useState(AUTO_REFRESH_INTERVAL);
  const [form] = Form.useForm();
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const stocksRef = useRef<Stock[]>([]);

  const fetchData = async () => {
    setLoading(true);
    try {
      const [stocksRes, portfoliosRes] = await Promise.all([
        getStocks(),
        getPortfolios(),
      ]);
      setStocks(stocksRes.data);
      stocksRef.current = stocksRes.data;
      setPortfolios(portfoliosRes.data);
    } catch {
      message.error('Ошибка загрузки данных');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchData();
  }, []);

  useEffect(() => {
    if (stocks.length === 0)
    {
      setSelectedStockId(null);
      return;
    }

    if (!selectedStockId || !stocks.some((stock) => stock.id === selectedStockId)) {
      setSelectedStockId(stocks[0].id);
    }
  }, [stocks, selectedStockId]);

  useEffect(() => {
    if (!selectedStockId) {
      setHistoryData([]);
      return;
    }

    const fetchHistory = async () => {
      setHistoryLoading(true);
      try {
        const res = await getStockHistory(selectedStockId, historyRange);
        setHistoryData(res.data);
        try {
          const eurUsdRes = await getEurUsdRate();
          setHistoryEurUsdRate(eurUsdRes.data.eurUsd);
        } catch {
          setHistoryEurUsdRate(null);
        }
      } catch {
        setHistoryData([]);
        setHistoryEurUsdRate(null);
        message.error('Ошибка загрузки исторических данных');
      } finally {
        setHistoryLoading(false);
      }
    };

    fetchHistory();
  }, [selectedStockId, historyRange]);

  const handleRefreshPrices = useCallback(async (silent = false) => {
    if (refreshing) return;
    setRefreshing(true);
    try {
      const currentStocks = stocksRef.current;
      const stocksWithTicker = currentStocks.filter((s) => s.ticker?.trim());

      setLivePrices((prev) => {
        const next = { ...prev };
        stocksWithTicker.forEach((stock) => {
          const current = prev[stock.id];
          next[stock.id] = {
            price: current?.price ?? null,
            priceEur: current?.priceEur ?? null,
            loading: true,
            marketState: current?.marketState,
          };
        });
        return next;
      });

      const eurUsdRes = await getEurUsdRate();
      const eurUsd = eurUsdRes.data.eurUsd;

      const results = await Promise.allSettled(
        stocksWithTicker.map(async (stock) => {
          try {
            const priceRes = await getStockPrice(stock.ticker);
            const priceUsd = priceRes.data.currentPrice;
            const priceEur = eurUsd > 0 ? priceUsd / eurUsd : priceUsd;
            const marketState: string = priceRes.data.marketState ?? 'CLOSED';

            setLivePrices((prev) => ({
              ...prev,
              [stock.id]: { price: priceUsd, priceEur, loading: false, marketState },
            }));

            await updateStock(stock.id, {
              ...stock,
              currentPrice: Math.round(priceEur * 100) / 100,
              updatedAt: new Date().toISOString(),
            });
          } catch (error) {
            setLivePrices((prev) => {
              const current = prev[stock.id];
              return {
                ...prev,
                [stock.id]: {
                  price: current?.price ?? null,
                  priceEur: current?.priceEur ?? null,
                  loading: false,
                  marketState: current?.marketState,
                },
              };
            });
            throw error;
          }
        })
      );
      const failed = results.filter((r) => r.status === 'rejected').length;
      await fetchData();
      if (!silent) {
        if (failed === 0) {
          message.success('Цены обновлены');
        } else {
          message.warning(`Цены обновлены частично (${failed} ошибок)`);
        }
      } else {
        message.info('Цены автоматически обновлены');
      }
    } catch {
      if (!silent) message.error('Ошибка обновления цен');
    } finally {
      setRefreshing(false);
    }
  }, [refreshing]);

  useEffect(() => {
    const autoRefreshTimer = setInterval(() => {
      handleRefreshPrices(true);
      setCountdown(AUTO_REFRESH_INTERVAL);
    }, AUTO_REFRESH_INTERVAL * 1000);
    return () => clearInterval(autoRefreshTimer);
  }, [handleRefreshPrices]);

  useEffect(() => {
    setCountdown(AUTO_REFRESH_INTERVAL);
    const countdownTimer = setInterval(() => {
      setCountdown((prev) => (prev <= 1 ? AUTO_REFRESH_INTERVAL : prev - 1));
    }, 1000);
    return () => clearInterval(countdownTimer);
  }, []);

  const formatCountdown = (seconds: number) => {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return `${m}:${s.toString().padStart(2, '0')}`;
  };

  const openCreateModal = () => {
    setEditingStock(null);
    form.resetFields();
    setModalOpen(true);
  };

  const openEditModal = (stock: Stock) => {
    setEditingStock(stock);
    form.setFieldsValue(stock);
    setModalOpen(true);
  };

  const handleSubmit = async (values: {
    ticker: string;
    name: string;
    currentPrice: number;
  }) => {
    setSubmitting(true);
    try {
      if (editingStock) {
        await updateStock(editingStock.id, {
          ...editingStock,
          ...values,
          exchange: '',
          updatedAt: new Date().toISOString(),
        });
        message.success('Акция обновлена');
      } else {
        await createStock({ ...values, exchange: '' });
        message.success('Акция добавлена');
      }
      setModalOpen(false);
      form.resetFields();
      fetchData();
    } catch {
      message.error('Ошибка сохранения акции');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await deleteStock(id);
      message.success('Акция удалена');
      fetchData();
    } catch {
      message.error('Ошибка удаления акции');
    }
  };

  const handleFetchLivePrice = async (stock: Stock) => {
    if (!stock.ticker?.trim()) return;
    setLivePrices((prev) => ({ ...prev, [stock.id]: { price: null, priceEur: null, loading: true } }));
    try {
      const [priceRes, eurUsdRes] = await Promise.all([
        getStockPrice(stock.ticker),
        getEurUsdRate(),
      ]);
      const priceUsd = priceRes.data.currentPrice;
      const eurUsd = eurUsdRes.data.eurUsd;
      const priceEur = eurUsd > 0 ? priceUsd / eurUsd : priceUsd;
      const marketState: string = priceRes.data.marketState ?? 'CLOSED';

      setLivePrices((prev) => ({
        ...prev,
        [stock.id]: { price: priceUsd, priceEur, loading: false, marketState },
      }));

      await updateStock(stock.id, {
        ...stock,
        currentPrice: Math.round(priceEur * 100) / 100,
        updatedAt: new Date().toISOString(),
      });

      setStocks((prev) =>
        prev.map((s) =>
          s.id === stock.id
            ? { ...s, currentPrice: Math.round(priceEur * 100) / 100, updatedAt: new Date().toISOString() }
            : s
        )
      );
    } catch {
      setLivePrices((prev) => ({ ...prev, [stock.id]: { price: null, priceEur: null, loading: false } }));
      message.error(`Ошибка получения цены для ${stock.ticker}`);
    }
  };

  const columns = [
    {
      title: 'Тикер',
      dataIndex: 'ticker',
      key: 'ticker',
      sorter: (a: Stock, b: Stock) => a.ticker.localeCompare(b.ticker),
      render: (ticker: string, record: Stock) => (
        <Button type="link" style={{ padding: 0, fontWeight: 600 }} onClick={() => setSelectedStockId(record.id)} aria-label={`Выбрать ${ticker} для просмотра графика`}>
          {ticker}
        </Button>
      ),
    },
    {
      title: 'Название',
      dataIndex: 'name',
      key: 'name',
    },
    {
      title: 'Текущая цена (€)',
      dataIndex: 'currentPrice',
      key: 'currentPrice',
      render: (v: number) => `€${v.toFixed(2)}`,
      sorter: (a: Stock, b: Stock) => a.currentPrice - b.currentPrice,
    },
    {
      title: 'Живая цена',
      key: 'livePrice',
      render: (_: unknown, record: Stock) => {
        const live = livePrices[record.id];
        const stateInfo = live?.marketState ? marketStateLabel[live.marketState] ?? { color: 'default', text: live.marketState } : null;
        return (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
            <span>
              {live?.loading
                ? '...'
                : live?.price != null
                ? `$${live.price.toFixed(2)} USD`
                : '—'}
            </span>
            {stateInfo && !live?.loading && (
              <Tag color={stateInfo.color}>{stateInfo.text}</Tag>
            )}
            <Button
              icon={<ReloadOutlined />}
              size="small"
              loading={live?.loading}
              disabled={!record.ticker?.trim()}
              onClick={() => handleFetchLivePrice(record)}
            />
          </div>
        );
      },
    },
    {
      title: 'Обновлено',
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      render: (v: string) => dayjs(v).format('DD.MM.YYYY HH:mm'),
    },
    {
      title: 'Действия',
      key: 'actions',
      render: (_: unknown, record: Stock) => (
        <div style={{ display: 'flex', gap: 8 }}>
          <Button
            icon={<EditOutlined />}
            size="small"
            onClick={() => openEditModal(record)}
          >
            Изменить
          </Button>
          <Popconfirm
            title="Удалить акцию?"
            onConfirm={() => handleDelete(record.id)}
            okText="Да"
            cancelText="Нет"
          >
            <Button icon={<DeleteOutlined />} size="small" danger>
              Удалить
            </Button>
          </Popconfirm>
        </div>
      ),
    },
  ];

  const xAxisFormatByRange: Record<StockHistoryRange, string> = {
    '5y': 'MM.YYYY',
    '3y': 'MM.YYYY',
    '1y': 'DD.MM.YY',
    '1w': 'DD.MM HH:mm',
    '24h': 'HH:mm',
    'today': 'HH:mm',
  };

  const historyChartData = useMemo(
    () => historyData.map((point) => ({ ...point, closeEur: historyEurUsdRate && historyEurUsdRate > 0 ? point.close / historyEurUsdRate : point.close })),
    [historyData, historyEurUsdRate],
  );

  const selectedStock = useMemo(
    () => stocks.find((stock) => stock.id === selectedStockId) ?? null,
    [stocks, selectedStockId],
  );

  const selectedStockCurrentPriceEur = selectedStockId
    ? (livePrices[selectedStockId]?.priceEur ?? selectedStock?.currentPrice ?? null)
    : null;
  const periodStartPriceEur = historyChartData.length > 0 ? historyChartData[0].closeEur : null;
  const periodChangeEur = periodStartPriceEur != null && selectedStockCurrentPriceEur != null
    ? selectedStockCurrentPriceEur - periodStartPriceEur
    : null;
  const periodChangePercent = periodChangeEur != null && periodStartPriceEur != null && periodStartPriceEur !== 0
    ? (periodChangeEur / periodStartPriceEur) * 100
    : null;
  const performanceColor = periodChangeEur == null ? undefined : (periodChangeEur >= 0 ? '#389e0d' : '#cf1322');
  const formatSigned = (value: number, suffix = '') => `${value >= 0 ? '+' : ''}${value.toFixed(2)}${suffix}`;

  const menuItems = [
    {
      key: 'dashboard',
      icon: <DashboardOutlined />,
      label: 'Dashboard',
      onClick: () => navigate('/'),
    },
    {
      key: 'portfolios',
      icon: <FolderOutlined />,
      label: 'Мои портфели',
      children: portfolios.map((p) => ({
        key: `portfolio-${p.id}`,
        label: p.name,
        onClick: () => navigate(`/portfolios/${p.id}`),
      })),
    },
    {
      key: 'stocks',
      icon: <StockOutlined />,
      label: 'Акции',
      onClick: () => navigate('/stocks'),
    },
    {
      key: 'profile',
      icon: <UserOutlined />,
      label: user?.username ?? 'Профиль',
    },
    {
      key: 'logout',
      icon: <LogoutOutlined />,
      label: 'Выйти',
      onClick: logout,
      danger: true,
    },
  ];

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider collapsible breakpoint="lg" collapsedWidth="0">
        <div style={{ color: '#fff', padding: '16px', fontSize: 18, fontWeight: 700 }}>
          💹 FinanceApp
        </div>
        <Menu
          theme="dark"
          mode="inline"
          defaultSelectedKeys={['stocks']}
          items={menuItems}
        />
      </Sider>
      <Layout>
        <Header style={{ background: '#fff', padding: '0 24px', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <Title level={4} style={{ margin: 0 }}>
            Акции
          </Title>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            <Text type="secondary" style={{ fontSize: 12 }}>
              Авто-обновление через {formatCountdown(countdown)}
            </Text>
            <Button
              icon={<ReloadOutlined />}
              loading={refreshing}
              onClick={() => { handleRefreshPrices(false); setCountdown(AUTO_REFRESH_INTERVAL); }}
            >
              Обновить цены
            </Button>
            <Button
              type="primary"
              icon={<PlusOutlined />}
              onClick={openCreateModal}
            >
              Добавить акцию
            </Button>
          </div>
        </Header>
        <Content style={{ padding: 24 }}>
          {loading ? (
            <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}>
              <Spin size="large" />
            </div>
          ) : (
            <div style={{ display: 'grid', gap: 16 }}>
              <Card
                title="История цены"
                extra={(
                  <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', justifyContent: 'flex-end' }}>
                    <Select
                      value={selectedStockId ?? undefined}
                      style={{ minWidth: 220 }}
                      placeholder="Выберите акцию"
                      options={stocks.map((stock) => ({ value: stock.id, label: `${stock.ticker} — ${stock.name}` }))}
                      onChange={(value) => setSelectedStockId(value)}
                    />
                    <Segmented
                      value={historyRange}
                      onChange={(value) => setHistoryRange(value as StockHistoryRange)}
                      options={[
                        { label: '5 лет', value: '5y' },
                        { label: '3 года', value: '3y' },
                        { label: '1 год', value: '1y' },
                        { label: '1 неделя', value: '1w' },
                        { label: '24 часа', value: '24h' },
                        { label: 'Сегодня', value: 'today' },
                      ]}
                    />
                  </div>
                )}
              >
                {historyLoading ? (
                  <div style={{ display: 'flex', justifyContent: 'center', padding: 40 }}>
                    <Spin />
                  </div>
                ) : historyData.length === 0 ? (
                  <Empty description="Нет данных для выбранного периода" />
                ) : (
                  <div style={{ display: 'grid', gap: 12 }}>
                    <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
                      <div style={{ minWidth: 220, padding: '8px 12px', border: '1px solid #f0f0f0', borderRadius: 8 }}>
                        <Text type="secondary" style={{ fontSize: 12 }}>
                          Изменение за период (к текущей цене)
                        </Text>
                        <div style={{ color: performanceColor ?? 'inherit', fontWeight: 600, marginTop: 4 }}>
                          {periodChangeEur == null
                            ? '—'
                            : `${formatSigned(periodChangeEur)} € (${periodChangePercent == null ? '—' : formatSigned(periodChangePercent, '%')})`}
                        </div>
                      </div>
                    </div>
                    <div style={{ width: '100%', height: 240 }}>
                      <ResponsiveContainer>
                        <LineChart data={historyChartData}>
                          <CartesianGrid strokeDasharray="3 3" />
                          <XAxis
                            dataKey="timestamp"
                            tickFormatter={(value: string) => dayjs(value).format(xAxisFormatByRange[historyRange])}
                          />
                          <YAxis
                            domain={['auto', 'auto']}
                            tickFormatter={(value: number) => `€${value.toFixed(2)}`}
                          />
                          <Tooltip
                            labelFormatter={(value: string) => dayjs(value).format('DD.MM.YYYY HH:mm')}
                            formatter={(value: number) => [`€${Number(value).toFixed(2)}`, 'Цена']}
                          />
                          <Line type="monotone" dataKey="closeEur" name="Close" stroke="#1677ff" dot={false} strokeWidth={2} />
                        </LineChart>
                      </ResponsiveContainer>
                    </div>
                  </div>
                )}
              </Card>

              <Table
                dataSource={stocks}
                columns={columns}
                rowKey="id"
                scroll={{ x: true }}
                pagination={{ pageSize: 20 }}
              />
            </div>
          )}
        </Content>
      </Layout>

      <Modal
        title={editingStock ? 'Редактировать акцию' : 'Добавить акцию'}
        open={modalOpen}
        onCancel={() => { setModalOpen(false); form.resetFields(); setEditingStock(null); }}
        footer={null}
      >
        <Form form={form} layout="vertical" onFinish={handleSubmit}>
          <Form.Item
            label="Тикер"
            name="ticker"
            rules={[{ required: true, message: 'Введите тикер' }]}
          >
            <Input placeholder="AAPL" />
          </Form.Item>
          <Form.Item
            label="Название"
            name="name"
            rules={[{ required: true, message: 'Введите название' }]}
          >
            <Input placeholder="Apple Inc." />
          </Form.Item>
          <Form.Item
            label="Текущая цена (€)"
            name="currentPrice"
            rules={[{ required: true, message: 'Введите текущую цену' }]}
          >
            <InputNumber
              min={0}
              step={0.01}
              style={{ width: '100%' }}
              placeholder="0.00"
              prefix="€"
            />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={submitting} block>
              {editingStock ? 'Сохранить' : 'Добавить'}
            </Button>
          </Form.Item>
        </Form>
      </Modal>
    </Layout>
  );
};

export default StocksPage;
