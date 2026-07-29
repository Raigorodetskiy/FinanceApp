import React, { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import {
  Layout,
  Table,
  Button,
  Modal,
  Form,
  Input,
  InputNumber,
  Select,
  Spin,
  Typography,
  Popconfirm,
  message,
  Tag,
  Tooltip,
} from 'antd';
import {
  PlusOutlined,
  EditOutlined,
  DeleteOutlined,
  ReloadOutlined,
  CaretRightFilled,
} from '@ant-design/icons';
import dayjs from 'dayjs';
import utc from 'dayjs/plugin/utc';
import {
  getStocks,
  createStock,
  updateStock,
  deleteStock,
  getPortfolios,
  getStockPrice,
} from '../services/api';
import AppSidebar from '../components/AppSidebar';
import StockPriceChart from '../components/StockPriceChart';
import { useAuth } from '../contexts/AuthContext';
import type { Stock, Portfolio, StockQuoteResponse, StockExchange } from '../types';
import { groupStocks } from '../utils/stockGrouping';

dayjs.extend(utc);

const { Header, Content } = Layout;
const { Title, Text } = Typography;

const AUTO_REFRESH_INTERVAL = 10 * 60; // 10 minutes in seconds

const COLOR_POSITIVE = '#389e0d';
const COLOR_NEGATIVE = '#cf1322';
const PORTFOLIO_ROW_CLASS = 'portfolio-stock-row';
const STOCK_TEXT_LOCALE = 'ru-RU';
const DEFAULT_STOCK_EXCHANGE: StockExchange = 'NYSE';
const exchangeLabelByValue: Record<StockExchange, string> = {
  NYSE: 'NYSE',
  Frankfurt: 'Frankfurt',
};
const exchangeAbbreviationByValue: Record<StockExchange, string> = {
  NYSE: 'NYSE',
  Frankfurt: 'FRA',
};
const exchangeOptions: { label: string; value: StockExchange }[] = [
  { label: exchangeLabelByValue.NYSE, value: 'NYSE' },
  { label: exchangeLabelByValue.Frankfurt, value: 'Frankfurt' },
];

const formatPercent24h = (pct: number): string => {
  const formatted = pct.toLocaleString(STOCK_TEXT_LOCALE, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  return pct > 0 ? `+${formatted} %` : `${formatted} %`;
};

const getPercent24hColor = (pct: number | null | undefined): string | undefined => {
  if (pct === null || pct === undefined || pct === 0) return undefined;
  return pct > 0 ? COLOR_POSITIVE : COLOR_NEGATIVE;
};

const getPercent24hText = (live: LivePriceEntry | undefined): string | null => {
  if (!live) {
    return null;
  }

  if (live.loading) {
    return '...';
  }

  if (live.quote?.percentChange === null || live.quote?.percentChange === undefined) {
    return '—';
  }

  return formatPercent24h(live.quote.percentChange);
};

const marketStateLabel: Record<string, { color: string; text: string }> = {
  REGULAR: { color: 'green', text: 'Open' },
  PRE:     { color: 'blue',  text: 'Pre-Market' },
  POST:    { color: 'orange', text: 'After-Hours' },
  CLOSED:  { color: 'default', text: 'Closed' },
};

const TICKER_COL_WIDTH = 220;
const NAME_COL_WIDTH = 320;
const CURRENT_PRICE_COL_WIDTH = 180;
const LIVE_PRICE_COL_WIDTH = 240;
const UPDATED_COL_WIDTH = 170;
const ACTIONS_COL_WIDTH = 120;
const TICKER_META_SPACE_WIDTH = 70;
const TICKER_TEXT_MAX_WIDTH = TICKER_COL_WIDTH - TICKER_META_SPACE_WIDTH;
const STOCKS_TABLE_SCROLL_X =
  TICKER_COL_WIDTH
  + NAME_COL_WIDTH
  + CURRENT_PRICE_COL_WIDTH
  + LIVE_PRICE_COL_WIDTH
  + UPDATED_COL_WIDTH
  + ACTIONS_COL_WIDTH;
const ELLIPSIS_STYLE: React.CSSProperties = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' };
const CELL_BASE_STYLE: React.CSSProperties = { display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 };
const CELL_NOWRAP_STYLE: React.CSSProperties = { ...CELL_BASE_STYLE, whiteSpace: 'nowrap' };
const FLEX_MIN_WIDTH_STYLE: React.CSSProperties = { minWidth: 0, flex: 1 };

type LivePriceEntry = {
  quote: StockQuoteResponse | null;
  loading: boolean;
};

type ChartRow = { _isChartRow: true; _stockId: number };
type TableRow = Stock | ChartRow;

const isChartRow = (record: TableRow): record is ChartRow => !!(record as ChartRow)._isChartRow;

const preserveEntry = (current: LivePriceEntry | undefined, loading: boolean): LivePriceEntry => ({
  quote: current?.quote ?? null,
  loading,
});

const StocksPage: React.FC = () => {
  const [stocks, setStocks] = useState<Stock[]>([]);
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingStock, setEditingStock] = useState<Stock | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [livePrices, setLivePrices] = useState<Record<number, LivePriceEntry>>({});
  const [expandedStockId, setExpandedStockId] = useState<number | null>(null);
  const [countdown, setCountdown] = useState(AUTO_REFRESH_INTERVAL);
  const [form] = Form.useForm();
  const { user, logout } = useAuth();
  const stocksRef = useRef<Stock[]>([]);
  const portfolioStockIds = useMemo(() => {
    const ids = new Set<number>();
    portfolios.forEach((portfolio) => {
      portfolio.items?.forEach((item) => {
        if ((item.stockId ?? 0) > 0) {
          ids.add(item.stockId);
        }
      });
    });
    return ids;
  }, [portfolios]);
  const { portfolioGroup, fraGroup, nyseGroup } = useMemo(
    () => groupStocks(stocks, portfolioStockIds),
    [stocks, portfolioStockIds],
  );

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

  const persistConvertedPrice = useCallback(async (stock: Stock, quote: StockQuoteResponse) => {
    if (quote.currentPriceEur == null) {
      return null;
    }

    const roundedCurrentPrice = Math.round(quote.currentPriceEur * 100) / 100;
    const updatedAt = new Date().toISOString();

    await updateStock(stock.id, {
      ...stock,
      currentPrice: roundedCurrentPrice,
      updatedAt,
    });

    return { roundedCurrentPrice, updatedAt };
  }, []);

  const handleRefreshPrices = useCallback(async (silent = false) => {
    if (refreshing) return;
    setRefreshing(true);
    try {
      const currentStocks = stocksRef.current;
      const stocksWithTicker = currentStocks.filter((s) => s.ticker?.trim());

      setLivePrices((prev) => {
        const next = { ...prev };
        stocksWithTicker.forEach((stock) => {
          next[stock.id] = preserveEntry(prev[stock.id], true);
        });
        return next;
      });

      const results = await Promise.allSettled(
        stocksWithTicker.map(async (stock) => {
          try {
            const priceRes = await getStockPrice(stock.ticker, stock.exchange);
            const quote = priceRes.data;

            setLivePrices((prev) => ({
              ...prev,
              [stock.id]: { quote, loading: false },
            }));

            await persistConvertedPrice(stock, quote);
          } catch (error) {
            setLivePrices((prev) => ({
              ...prev,
              [stock.id]: preserveEntry(prev[stock.id], false),
            }));
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
  }, [persistConvertedPrice, refreshing]);

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
    form.setFieldsValue({ exchange: DEFAULT_STOCK_EXCHANGE });
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
    commonName?: string;
    exchange: StockExchange;
    currentPrice: number;
    wkn?: string;
    isin?: string;
  }) => {
    // Normalize: blank → null, trim + uppercase
    const normalizeId = (v?: string): string | null => {
      const s = (v ?? '').trim().toUpperCase();
      return s.length > 0 ? s : null;
    };
    const wkn = normalizeId(values.wkn);
    const isin = normalizeId(values.isin);
    const normalizedName = values.name.trim();
    const normalizedCommonName = (values.commonName ?? '').trim() || normalizedName;

    setSubmitting(true);
    try {
      if (editingStock) {
        await updateStock(editingStock.id, {
          ...editingStock,
          ...values,
          name: normalizedName,
          commonName: normalizedCommonName,
          wkn,
          isin,
          exchange: values.exchange,
          updatedAt: new Date().toISOString(),
        });
        message.success('Акция обновлена');
      } else {
        await createStock({
          ...values,
          name: normalizedName,
          commonName: normalizedCommonName,
          wkn,
          isin,
          exchange: values.exchange,
        });
        message.success('Акция добавлена');
      }
      setModalOpen(false);
      form.resetFields();
      fetchData();
    } catch (err: unknown) {
      const errorMsg =
        err != null &&
        typeof err === 'object' &&
        'response' in err &&
        err.response != null &&
        typeof err.response === 'object' &&
        'data' in err.response &&
        typeof err.response.data === 'string'
          ? err.response.data
          : 'Ошибка сохранения акции';
      message.error(errorMsg);
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
    setLivePrices((prev) => ({ ...prev, [stock.id]: preserveEntry(prev[stock.id], true) }));
    try {
      const priceRes = await getStockPrice(stock.ticker, stock.exchange);
      const quote = priceRes.data;

      setLivePrices((prev) => ({
        ...prev,
        [stock.id]: { quote, loading: false },
      }));

      const persisted = await persistConvertedPrice(stock, quote);

      if (persisted != null) {
        setStocks((prev) =>
          prev.map((s) =>
            s.id === stock.id
              ? { ...s, currentPrice: persisted.roundedCurrentPrice, updatedAt: persisted.updatedAt }
              : s
          )
        );
      }
    } catch {
      setLivePrices((prev) => ({ ...prev, [stock.id]: preserveEntry(prev[stock.id], false) }));
      message.error(`Ошибка получения цены для ${stock.ticker}`);
    }
  };

  const TOTAL_COLS = 6;

  const columns = [
    {
      title: 'Тикер',
      dataIndex: 'ticker',
      key: 'ticker',
      width: TICKER_COL_WIDTH,
      render: (_ticker: string, record: TableRow) => {
        if (isChartRow(record)) {
          const stock = stocks.find((s) => s.id === record._stockId);
          const live = livePrices[record._stockId];
          return {
            children: (
              <StockPriceChart
                panelId={`chart-panel-${record._stockId}`}
                stockId={record._stockId}
                ticker={stock?.ticker ?? ''}
                name={stock?.name ?? ''}
                wkn={stock?.wkn ?? null}
                isin={stock?.isin ?? null}
                liveQuote={live?.quote ?? null}
                storedPriceEur={stock?.currentPrice ?? null}
              />
            ),
            props: { colSpan: TOTAL_COLS },
          };
        }
        const stock = record as Stock;
        const isExpanded = expandedStockId === stock.id;
        return (
          <div style={CELL_NOWRAP_STYLE}>
            <Tooltip title={stock.ticker}>
              <button
                type="button"
                onClick={() => handleTickerClick(stock.id)}
                aria-expanded={isExpanded}
                aria-controls={`chart-panel-${stock.id}`}
                aria-label={isExpanded ? `Закрыть график цены: ${stock.ticker}` : `Открыть график цены: ${stock.ticker}`}
                style={{
                  padding: 0,
                  background: 'none',
                  border: 'none',
                  cursor: 'pointer',
                  fontWeight: 600,
                  color: isExpanded ? '#1677ff' : 'inherit',
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 4,
                  minWidth: 0,
                  maxWidth: TICKER_TEXT_MAX_WIDTH,
                }}
              >
                <CaretRightFilled
                  style={{
                    fontSize: 10,
                    transition: 'transform 0.2s',
                    transform: isExpanded ? 'rotate(90deg)' : 'rotate(0deg)',
                    color: '#1677ff',
                    flex: '0 0 auto',
                  }}
                />
                <span style={ELLIPSIS_STYLE}>
                  {stock.ticker}
                </span>
              </button>
            </Tooltip>
            <Tooltip title={exchangeLabelByValue[stock.exchange]}>
              <Tag style={{ marginInlineEnd: 0 }}>{exchangeAbbreviationByValue[stock.exchange]}</Tag>
            </Tooltip>
          </div>
        );
      },
    },
    {
      title: 'Название',
      dataIndex: 'name',
      key: 'name',
      width: NAME_COL_WIDTH,
      render: (name: string, record: TableRow) => {
        if (isChartRow(record)) return { children: null, props: { colSpan: 0 } };
        const stock = record as Stock;
        return (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            <div style={CELL_BASE_STYLE}>
              <Text style={FLEX_MIN_WIDTH_STYLE} ellipsis={{ tooltip: name }}>{name}</Text>
              {portfolioStockIds.has(stock.id) && (
                <Tag color="green">
                  В портфеле
                </Tag>
              )}
            </div>
            {stock.commonName && stock.commonName !== name && (
              <Text type="secondary" style={{ fontSize: 12 }} ellipsis={{ tooltip: stock.commonName }}>
                {stock.commonName}
              </Text>
            )}
          </div>
        );
      },
    },
    {
      title: 'Текущая цена (€)',
      dataIndex: 'currentPrice',
      key: 'currentPrice',
      width: CURRENT_PRICE_COL_WIDTH,
      render: (v: number, record: TableRow) => {
        if (isChartRow(record)) return { children: null, props: { colSpan: 0 } };
        const stock = record as Stock;
        const live = livePrices[stock.id];
        const pct = live?.quote?.percentChange;
        const pctColor = getPercent24hColor(pct);
        const displayPrice = live?.quote?.currentPriceEur ?? v;
        const percentText = getPercent24hText(live);
        return (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
            <span style={{ whiteSpace: 'nowrap' }}>€{displayPrice.toFixed(2)}</span>
            {percentText && (
              <span style={{ color: pctColor, fontWeight: 500, whiteSpace: 'nowrap' }}>
                {percentText}
              </span>
            )}
          </div>
        );
      },
    },
    {
      title: 'Живая цена',
      key: 'livePrice',
      width: LIVE_PRICE_COL_WIDTH,
      render: (_: unknown, record: TableRow) => {
        if (isChartRow(record)) return { children: null, props: { colSpan: 0 } };
        const stock = record as Stock;
        const live = livePrices[stock.id];
        const quote = live?.quote ?? null;
        const stateInfo = quote?.marketState ? marketStateLabel[quote.marketState] ?? { color: 'default', text: quote.marketState } : null;
        const rawQuoteText = quote
          ? `${quote.rawCurrentPrice.toFixed(2)} ${quote.currency ?? quote.normalizedQuoteCurrency ?? '—'}`
          : '—';
        const normalizedTooltip =
          quote && quote.quoteUnitMultiplier !== 1 && quote.normalizedQuoteCurrency
            ? `Нормализовано: ${quote.normalizedCurrentPrice.toFixed(3)} ${quote.normalizedQuoteCurrency}`
            : undefined;
        return (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
            <span title={normalizedTooltip}>
              {live?.loading
                ? '...'
                : rawQuoteText}
            </span>
            {stateInfo && !live?.loading && (
              <Tag color={stateInfo.color}>{stateInfo.text}</Tag>
            )}
            {quote?.conversionWarning && !live?.loading && (
              <Tag color="gold">Нет EUR</Tag>
            )}
            <Button
              icon={<ReloadOutlined />}
              size="small"
              loading={live?.loading}
              disabled={!stock.ticker?.trim()}
              onClick={() => handleFetchLivePrice(stock)}
            />
          </div>
        );
      },
    },
    {
      title: 'Обновлено',
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      width: UPDATED_COL_WIDTH,
      render: (v: string, record: TableRow) => {
        if (isChartRow(record)) return { children: null, props: { colSpan: 0 } };
        return <span style={{ whiteSpace: 'nowrap' }}>{dayjs.utc(v).local().format('DD.MM.YYYY HH:mm')}</span>;
      },
    },
    {
      title: 'Действия',
      key: 'actions',
      width: ACTIONS_COL_WIDTH,
      render: (_: unknown, record: TableRow) => {
        if (isChartRow(record)) return { children: null, props: { colSpan: 0 } };
        const stock = record as Stock;
        return (
          <div style={{ display: 'flex', gap: 8, flexWrap: 'nowrap', whiteSpace: 'nowrap' }}>
            <Tooltip title="Изменить">
              <Button
                icon={<EditOutlined />}
                size="small"
                aria-label="Изменить"
                onClick={() => openEditModal(stock)}
              />
            </Tooltip>
            <Popconfirm
              title="Удалить акцию?"
              onConfirm={() => handleDelete(stock.id)}
              okText="Да"
              cancelText="Нет"
            >
              <Tooltip title="Удалить">
                <Button icon={<DeleteOutlined />} size="small" danger aria-label="Удалить" />
              </Tooltip>
            </Popconfirm>
          </div>
        );
      },
    },
  ];

  const makeGroupRows = useCallback((group: Stock[]): TableRow[] => {
    const rows: TableRow[] = [];
    for (const stock of group) {
      rows.push(stock);
      if (expandedStockId === stock.id) {
        rows.push({ _isChartRow: true, _stockId: stock.id });
      }
    }
    return rows;
  }, [expandedStockId]);

  const portfolioRows = useMemo(() => makeGroupRows(portfolioGroup), [makeGroupRows, portfolioGroup]);
  const fraRows = useMemo(() => makeGroupRows(fraGroup), [makeGroupRows, fraGroup]);
  const nyseRows = useMemo(() => makeGroupRows(nyseGroup), [makeGroupRows, nyseGroup]);

  const handleTickerClick = (stockId: number) => {
    setExpandedStockId((prev) => (prev === stockId ? null : stockId));
  };

  const getTableRowKey = useCallback(
    (record: TableRow) => isChartRow(record) ? `chart-${record._stockId}` : String((record as Stock).id),
    [],
  );

  const renderGroup = (groupTitle: string, groupStocks: Stock[], rows: TableRow[]) => {
    if (groupStocks.length === 0) return null;
    return (
      <div key={groupTitle} style={{ marginBottom: 24, border: '1px solid #d9d9d9', borderRadius: 8, overflow: 'hidden' }}>
        <div style={{ padding: '10px 16px', borderBottom: '1px solid #d9d9d9', background: '#fafafa', display: 'flex', alignItems: 'center', gap: 8 }}>
          <Title level={5} style={{ margin: 0 }}>{groupTitle}</Title>
          <Tag>{groupStocks.length}</Tag>
        </div>
        <Table
          className="stocks-table"
          dataSource={rows}
          columns={columns}
          rowKey={getTableRowKey}
          tableLayout="fixed"
          scroll={{ x: STOCKS_TABLE_SCROLL_X }}
          pagination={false}
          rowClassName={(record: TableRow) => {
            if (isChartRow(record)) return 'chart-panel-row';
            return portfolioStockIds.has((record as Stock).id) ? PORTFOLIO_ROW_CLASS : '';
          }}
        />
      </div>
    );
  };

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <AppSidebar
        portfolios={portfolios}
        selectedKeys={['stocks']}
        userName={user?.username}
        onLogout={logout}
      />
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
            <>
              {renderGroup('Портфель', portfolioGroup, portfolioRows)}
              {renderGroup('FRA', fraGroup, fraRows)}
              {renderGroup('NYSE', nyseGroup, nyseRows)}
            </>
          )}
        </Content>
      </Layout>

      <Modal
        title={editingStock ? 'Редактировать акцию' : 'Добавить акцию'}
        open={modalOpen}
        onCancel={() => { setModalOpen(false); form.resetFields(); setEditingStock(null); }}
        footer={null}
      >
        <Form
          form={form}
          layout="vertical"
          initialValues={{ exchange: DEFAULT_STOCK_EXCHANGE }}
          onFinish={handleSubmit}
        >
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
            label="Общее название"
            name="commonName"
            extra="Используется для обозначения одной и той же компании/бумаги на разных биржах."
          >
            <Input placeholder="Если оставить пустым, будет использовано поле «Название»" />
          </Form.Item>
          <Form.Item
            label="Биржа"
            name="exchange"
            rules={[{ required: true, message: 'Выберите биржу' }]}
          >
            <Select options={exchangeOptions} />
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
          <Form.Item
            label="WKN"
            name="wkn"
            rules={[
              {
                validator: (_, value: string | undefined) => {
                  const v = (value ?? '').trim().toUpperCase();
                  if (v.length === 0) return Promise.resolve();
                  if (/^[A-Z0-9]{6}$/.test(v)) return Promise.resolve();
                  return Promise.reject(new Error('WKN: ровно 6 буквенно-цифровых символов'));
                },
              },
            ]}
          >
            <Input
              placeholder="865985"
              maxLength={6}
              onChange={(e) => {
                form.setFieldValue('wkn', e.target.value.toUpperCase());
              }}
            />
          </Form.Item>
          <Form.Item
            label="ISIN"
            name="isin"
            rules={[
              {
                validator: (_, value: string | undefined) => {
                  const v = (value ?? '').trim().toUpperCase();
                  if (v.length === 0) return Promise.resolve();
                  if (/^[A-Z]{2}[A-Z0-9]{10}$/.test(v)) return Promise.resolve();
                  return Promise.reject(new Error('ISIN: 2 буквы страны + 10 буквенно-цифровых символов'));
                },
              },
            ]}
          >
            <Input
              placeholder="US0378331005"
              maxLength={12}
              onChange={(e) => {
                form.setFieldValue('isin', e.target.value.toUpperCase());
              }}
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
