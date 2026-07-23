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
import utc from 'dayjs/plugin/utc';
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

dayjs.extend(utc);

const { Sider, Header, Content } = Layout;
const { Title, Text } = Typography;

const AUTO_REFRESH_INTERVAL = 10 * 60; // 10 minutes in seconds
// For 1w history only break on long weekend-like gaps (≥40 h); overnight pauses stay connected.
const WEEK_GAP_THRESHOLD_MS = 40 * 60 * 60 * 1000;
// For 24h/today views treat large market-closure gaps as line breaks.
const SHORT_INTRADAY_GAP_THRESHOLD_MS = 2 * 60 * 60 * 1000;
// Minimal positive offset so Recharts treats the inserted null point as a distinct timestamp.
const MIN_GAP_MARKER_OFFSET_MS = 1;
const historyGapThresholdMsByRange: Partial<Record<StockHistoryRange, number>> = {
  '1w': WEEK_GAP_THRESHOLD_MS,
  '24h': SHORT_INTRADAY_GAP_THRESHOLD_MS,
  today: SHORT_INTRADAY_GAP_THRESHOLD_MS,
};
const formatSigned = (value: number, suffix = '') => `${value >= 0 ? '+' : ''}${value.toFixed(2)}${suffix}`;

const COLOR_POSITIVE = '#389e0d';
const COLOR_NEGATIVE = '#cf1322';

const formatPercent24h = (pct: number): string => {
  const formatted = pct.toLocaleString('ru-RU', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  return pct > 0 ? `+${formatted} %` : `${formatted} %`;
};

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
  const [livePrices, setLivePrices] = useState<Record<number, { price: number | null; priceEur: number | null; loading: boolean; marketState?: string; percentChange24h?: number | null }>>({});
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
          message.warning('Не удалось получить курс EUR/USD. История отображается в USD.');
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
            percentChange24h: current?.percentChange24h ?? null,
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
            const percentChange24h: number | null = priceRes.data.percentChange ?? null;

            setLivePrices((prev) => ({
              ...prev,
              [stock.id]: { price: priceUsd, priceEur, loading: false, marketState, percentChange24h },
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
                  percentChange24h: current?.percentChange24h ?? null,
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
    setLivePrices((prev) => {
      const current = prev[stock.id];
      return { ...prev, [stock.id]: { price: current?.price ?? null, priceEur: current?.priceEur ?? null, loading: true, marketState: current?.marketState, percentChange24h: current?.percentChange24h ?? null } };
    });
    try {
      const [priceRes, eurUsdRes] = await Promise.all([
        getStockPrice(stock.ticker),
        getEurUsdRate(),
      ]);
      const priceUsd = priceRes.data.currentPrice;
      const eurUsd = eurUsdRes.data.eurUsd;
      const priceEur = eurUsd > 0 ? priceUsd / eurUsd : priceUsd;
      const marketState: string = priceRes.data.marketState ?? 'CLOSED';
      const percentChange24h: number | null = priceRes.data.percentChange ?? null;

      setLivePrices((prev) => ({
        ...prev,
        [stock.id]: { price: priceUsd, priceEur, loading: false, marketState, percentChange24h },
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
      setLivePrices((prev) => {
        const current = prev[stock.id];
        return { ...prev, [stock.id]: { price: current?.price ?? null, priceEur: current?.priceEur ?? null, loading: false, marketState: current?.marketState, percentChange24h: current?.percentChange24h ?? null } };
      });
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
        const pct = live?.percentChange24h;
        const pctColor = (pct === null || pct === undefined) ? undefined : pct > 0 ? COLOR_POSITIVE : pct < 0 ? COLOR_NEGATIVE : undefined;
        return (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
            <span>
              {live?.loading
                ? '...'
                : live?.price != null
                ? `$${live.price.toFixed(2)} USD`
                : '—'}
            </span>
            {!live?.loading && pct !== null && pct !== undefined && (
              <span style={{ color: pctColor, fontWeight: 500, whiteSpace: 'nowrap' }}>
                {formatPercent24h(pct)}
              </span>
            )}
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
      render: (v: string) => dayjs.utc(v).local().format('DD.MM.YYYY HH:mm'),
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
    '6m': 'DD.MM.YY',
    '3m': 'DD.MM.YY',
    '1m': 'DD',
    '1w': 'DD.MM HH:mm',
    '24h': 'HH:mm',
    'today': 'HH:mm',
  };

  const historyHasEurConversion = historyEurUsdRate != null && historyEurUsdRate > 0;
  const historyCurrencyCode = historyHasEurConversion ? 'EUR' : 'USD';
  const historyCurrencySymbol = historyHasEurConversion ? '€' : '$';
  const convertedHistoryRate = historyHasEurConversion ? historyEurUsdRate : null;
  type HistoryChartPoint = {
    timestamp: string;
    timestampMs: number;
    closeChart: number | null;
  };
  const historyChartData = useMemo(
    () => {
      const sortedPoints: HistoryChartPoint[] = historyData
        .map((point) => ({
          timestamp: point.timestamp,
          timestampMs: dayjs.utc(point.timestamp).valueOf(),
          closeChart: convertedHistoryRate ? point.close / convertedHistoryRate : point.close,
        }))
        .sort((left, right) => left.timestampMs - right.timestampMs);

      const gapThresholdMs = historyGapThresholdMsByRange[historyRange];
      if (!gapThresholdMs || sortedPoints.length < 2) {
        return sortedPoints;
      }

      const pointsWithGaps: HistoryChartPoint[] = [sortedPoints[0]];
      let previousPoint = sortedPoints[0];
      for (let i = 1; i < sortedPoints.length; i += 1) {
        const currentPoint = sortedPoints[i];
        const gapMs = currentPoint.timestampMs - previousPoint.timestampMs;

        if (gapMs > gapThresholdMs) {
          // Keep marker just after the last real point so Recharts registers a distinct null marker and breaks the line.
          const gapTimestampMs = previousPoint.timestampMs + MIN_GAP_MARKER_OFFSET_MS;
          pointsWithGaps.push({
            timestamp: dayjs(gapTimestampMs).toISOString(),
            timestampMs: gapTimestampMs,
            closeChart: null,
          });
        }

        pointsWithGaps.push(currentPoint);
        previousPoint = currentPoint;
      }

      return pointsWithGaps;
    },
    [historyData, convertedHistoryRate, historyRange],
  );

  const selectedStock = useMemo(
    () => stocks.find((stock) => stock.id === selectedStockId) ?? null,
    [stocks, selectedStockId],
  );

  const selectedStockCurrentPriceEur = selectedStockId
    ? (livePrices[selectedStockId]?.priceEur ?? selectedStock?.currentPrice ?? null)
    : null;
  const periodStartPriceEur = useMemo(() => {
    if (!historyHasEurConversion) {
      return null;
    }

    for (const point of historyChartData) {
      if (point.closeChart != null) {
        return point.closeChart;
      }
    }

    return null;
  }, [historyChartData, historyHasEurConversion]);
  const periodChangeEur = periodStartPriceEur != null && selectedStockCurrentPriceEur != null
    ? selectedStockCurrentPriceEur - periodStartPriceEur
    : null;
  const periodChangePercent = periodChangeEur != null && periodStartPriceEur != null && periodStartPriceEur !== 0
    ? (periodChangeEur / periodStartPriceEur) * 100
    : null;
  const performanceColor = periodChangeEur == null ? undefined : (periodChangeEur >= 0 ? COLOR_POSITIVE : COLOR_NEGATIVE);

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
                        { label: '6 месяцев', value: '6m' },
                        { label: '3 месяца', value: '3m' },
                        { label: '1 месяц', value: '1m' },
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
                            : `€${formatSigned(periodChangeEur)} (${periodChangePercent == null ? '—' : formatSigned(periodChangePercent, '%')})`}
                        </div>
                      </div>
                    </div>
                    <div style={{ width: '100%', height: 240 }}>
                      <ResponsiveContainer>
                        <LineChart data={historyChartData}>
                          <CartesianGrid strokeDasharray="3 3" />
                          <XAxis
                            type="number"
                            dataKey="timestampMs"
                            scale="time"
                            domain={['dataMin', 'dataMax']}
                            tickFormatter={(value: number) => dayjs.utc(value).local().format(xAxisFormatByRange[historyRange])}
                          />
                          <YAxis
                            domain={['auto', 'auto']}
                            tickFormatter={(value: number) => `${historyCurrencySymbol}${value.toFixed(2)}`}
                          />
                          <Tooltip
                            labelFormatter={(value: number) => dayjs.utc(value).local().format('DD.MM.YYYY HH:mm')}
                            formatter={(value) => (
                              value === null
                                ? ['Нет данных', 'Цена']
                                : [`${historyCurrencySymbol}${Number(value).toFixed(2)}`, 'Цена']
                            )}
                          />
                          <Line type="monotone" dataKey="closeChart" name={`Close (${historyCurrencyCode})`} stroke="#1677ff" dot={false} strokeWidth={2} connectNulls={false} />
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
