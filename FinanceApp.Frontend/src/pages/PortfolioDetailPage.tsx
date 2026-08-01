import React, { useState, useEffect, useCallback, useMemo } from 'react';
import {
  Table,
  Button,
  Modal,
  Form,
  InputNumber,
  Select,
  DatePicker,
  Spin,
  Typography,
  Card,
  Row,
  Col,
  Tag,
  Tabs,
  Popconfirm,
  Tooltip,
  message,
  Input,
} from 'antd';
import {
  PlusOutlined,
  ArrowLeftOutlined,
  EditOutlined,
  DeleteOutlined,
  BellOutlined,
  CaretRightFilled,
  CheckOutlined,
  CloseOutlined,
} from '@ant-design/icons';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import dayjs, { type Dayjs } from 'dayjs';
import utc from 'dayjs/plugin/utc';
import {
  getPortfolio,
  getStocks,
  addPortfolioItem,
  updatePortfolioItem,
  deletePortfolioItem,
  getPortfolios,
  getOrders,
  createOrder,
  updateOrder,
  deleteOrder,
  getBalance,
  updateBalance,
  getTransactions,
  createTransaction,
  updateTransaction,
  deleteTransaction,
} from '../services/api';
import AuthenticatedShell from '../components/AuthenticatedShell';
import StockPriceChart from '../components/StockPriceChart';
import { useAuth } from '../contexts/AuthContext';
import type {
  Portfolio,
  Stock,
  PortfolioItem,
  Order,
  OrderType,
  OrderStatus,
  Transaction,
  TransactionType,
  PortfolioBalance,
} from '../types';

dayjs.extend(utc);

const { Title, Text } = Typography;

type PositionChartRow = { _isPositionChartRow: true; _itemId: number; _stockId: number };
type PositionTableRow = PortfolioItem | PositionChartRow;
const isPositionChartRow = (row: PositionTableRow): row is PositionChartRow =>
  !!(row as PositionChartRow)._isPositionChartRow;
const TOTAL_POS_COLS = 9;

const ORDER_TYPE_LABELS: Record<OrderType, string> = { Buy: 'Покупка', Sell: 'Продажа' };
const ORDER_STATUS_LABELS: Record<OrderStatus, string> = { Pending: 'Ожидание', Executed: 'Выполнено', Cancelled: 'Отменено' };
const ORDER_STATUS_COLORS: Record<OrderStatus, string> = { Pending: 'gold', Executed: 'green', Cancelled: 'red' };
const ORDER_TYPE_COLORS: Record<OrderType, string> = { Buy: 'blue', Sell: 'volcano' };

const TX_TYPE_LABELS: Record<TransactionType, string> = {
  Deposit: 'Пополнение',
  Withdrawal: 'Вывод',
  Buy: 'Покупка',
  Sell: 'Продажа',
  Dividend: 'Дивиденды',
};

const TX_TYPE_COLORS: Record<TransactionType, string> = {
  Deposit: 'green',
  Withdrawal: 'red',
  Buy: 'blue',
  Sell: 'cyan',
  Dividend: 'purple',
};

type BalanceField = 'cashBalance';

const getEffectiveSignedAmount = (t: Transaction) => {
  if (t.signedAmount !== 0 || t.amount === 0) return t.signedAmount;
  return t.type === 'Deposit' ? t.amount : -t.amount;
};

const getTransactionDescription = (t: Transaction): string => {
  if (t.stock) {
    return `${TX_TYPE_LABELS[t.type]} — ${t.stock.ticker} · ${t.stock.name}`;
  }
  return t.description ?? TX_TYPE_LABELS[t.type];
};

const formatCurrency = (value: number) => `${value < 0 ? '-€' : '€'}${Math.abs(value).toFixed(2)}`;

const PortfolioDetailPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const [searchParams] = useSearchParams();
  // Supported sections: positions | transactions
  const section = searchParams.get('section') ?? 'positions';

  const [portfolio, setPortfolio] = useState<Portfolio | null>(null);
  const [stocks, setStocks] = useState<Stock[]>([]);
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);

  // Finance state
  const [balance, setBalance] = useState<PortfolioBalance | null>(null);
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [txTypeFilter, setTxTypeFilter] = useState<TransactionType | 'all'>('all');
  const [financeLoaded, setFinanceLoaded] = useState(false);

  // Transaction modal
  const [txModalOpen, setTxModalOpen] = useState(false);
  const [editingTx, setEditingTx] = useState<Transaction | null>(null);
  const [txSubmitting, setTxSubmitting] = useState(false);
  const [txForm] = Form.useForm();
  const [balanceEditField, setBalanceEditField] = useState<BalanceField | null>(null);
  const [balanceDraft, setBalanceDraft] = useState({ cashBalance: 0 });
  const [balanceSubmitting, setBalanceSubmitting] = useState(false);

  // Position modal
  const [posModalOpen, setPosModalOpen] = useState(false);
  const [editingItem, setEditingItem] = useState<PortfolioItem | null>(null);
  const [posSubmitting, setPosSubmitting] = useState(false);
  const [posForm] = Form.useForm();

  // Position chart
  const [expandedPositionId, setExpandedPositionId] = useState<number | null>(null);

  // Order modal
  const [orderModalOpen, setOrderModalOpen] = useState(false);
  const [editingOrder, setEditingOrder] = useState<Order | null>(null);
  const [orderSubmitting, setOrderSubmitting] = useState(false);
  const [orderForm] = Form.useForm();

  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const fetchData = async () => {
    if (!id) return;
    setLoading(true);
    try {
      const [portfolioRes, stocksRes, portfoliosRes, ordersRes] = await Promise.all([
        getPortfolio(Number(id)),
        getStocks(),
        getPortfolios(),
        getOrders(Number(id)),
      ]);
      setPortfolio(portfolioRes.data);
      setStocks(stocksRes.data);
      setPortfolios(portfoliosRes.data);
      setOrders(ordersRes.data);
    } catch {
      message.error('Ошибка загрузки данных');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { fetchData(); setFinanceLoaded(false); }, [id]);

  const fetchFinanceData = useCallback(async () => {
    if (!id) return;
    try {
      const [balanceRes, txRes] = await Promise.all([
        getBalance(Number(id)),
        getTransactions(Number(id)),
      ]);
      setBalance(balanceRes.data);
      setTransactions(txRes.data);
      setFinanceLoaded(true);
    } catch {
      message.error('Ошибка загрузки финансовых данных');
    }
  }, [id]);

  useEffect(() => {
    if (section === 'transactions' && !financeLoaded) {
      fetchFinanceData();
    }
  }, [section, id, financeLoaded, fetchFinanceData]);

  useEffect(() => {
    setExpandedPositionId(null);
  }, [id, section]);

  useEffect(() => {
    if (balance) {
      setBalanceDraft({
        cashBalance: balance.cashBalance,
      });
    }
  }, [balance]);

  // ── Positions ──────────────────────────────────────────────
  const openAddPosModal = () => { setEditingItem(null); posForm.resetFields(); setPosModalOpen(true); };
  const openEditPosModal = (item: PortfolioItem) => {
    setEditingItem(item);
    posForm.setFieldsValue({ stockId: item.stockId, quantity: item.quantity, buyPrice: item.buyPrice });
    setPosModalOpen(true);
  };
  const handlePosSubmit = async (values: { stockId: number; quantity: number; buyPrice: number }) => {
    if (!id) return;
    setPosSubmitting(true);
    try {
      if (editingItem) {
        await updatePortfolioItem(Number(id), editingItem.id, values);
        message.success('Позиция обновлена');
      } else {
        await addPortfolioItem(Number(id), values);
        message.success('Позиция добавлена');
      }
      setPosModalOpen(false); posForm.resetFields(); fetchData();
    } catch { message.error('Ошибка сохранения позиции'); }
    finally { setPosSubmitting(false); }
  };
  const handleDeleteItem = async (itemId: number) => {
    if (!id) return;
    try { await deletePortfolioItem(Number(id), itemId); message.success('Позиция удалена'); fetchData(); }
    catch { message.error('Ошибка удаления позиции'); }
  };

  // ── Orders ─────────────────────────────────────────────────
  const openAddOrderModal = () => { setEditingOrder(null); orderForm.resetFields(); setOrderModalOpen(true); };
  const openEditOrderModal = (order: Order) => {
    setEditingOrder(order);
    orderForm.setFieldsValue({
      stockId: order.stockId,
      type: order.type,
      status: order.status,
      quantity: order.quantity,
      price: order.price,
      stopLoss: order.stopLoss ?? undefined,
      stopMarket: order.stopMarket ?? undefined,
    });
    setOrderModalOpen(true);
  };
  const handleOrderSubmit = async (values: {
    stockId: number; type: OrderType; status: OrderStatus;
    quantity: number; price: number; stopLoss?: number; stopMarket?: number;
  }) => {
    if (!id) return;
    setOrderSubmitting(true);
    try {
      if (editingOrder) {
        await updateOrder(Number(id), editingOrder.id, {
          type: values.type, status: values.status,
          quantity: values.quantity, price: values.price,
          stopLoss: values.stopLoss, stopMarket: values.stopMarket,
        });
        message.success('Ордер обновлён');
      } else {
        await createOrder(Number(id), {
          stockId: values.stockId, type: values.type,
          quantity: values.quantity, price: values.price,
          stopLoss: values.stopLoss, stopMarket: values.stopMarket,
        });
        message.success('Ордер создан');
      }
      setOrderModalOpen(false); orderForm.resetFields(); fetchData();
      // Also refresh finance if we're on transactions section (executed order creates tx)
      if (financeLoaded) fetchFinanceData();
    } catch { message.error('Ошибка сохранения ордера'); }
    finally { setOrderSubmitting(false); }
  };
  const handleDeleteOrder = async (orderId: number) => {
    if (!id) return;
    try { await deleteOrder(Number(id), orderId); message.success('Ордер удалён'); fetchData(); }
    catch { message.error('Ошибка удаления ордера'); }
  };

  const isTriggered = (order: Order): string | null => {
    const currentPrice = order.stock?.currentPrice;
    if (!currentPrice || order.status !== 'Pending') return null;
    if (order.stopLoss != null) {
      if (order.type === 'Buy' && currentPrice <= order.stopLoss) return `Цена ${currentPrice} достигла Stop Loss ${order.stopLoss}`;
      if (order.type === 'Sell' && currentPrice <= order.stopLoss) return `Цена ${currentPrice} достигла Stop Loss ${order.stopLoss}`;
    }
    if (order.stopMarket != null) {
      if (order.type === 'Buy' && currentPrice >= order.stopMarket) return `Цена ${currentPrice} достигла Stop Market ${order.stopMarket}`;
      if (order.type === 'Sell' && currentPrice >= order.stopMarket) return `Цена ${currentPrice} достигла Stop Market ${order.stopMarket}`;
    }
    if (order.type === 'Buy' && currentPrice <= order.price) return `Цена ${currentPrice} <= лимит покупки ${order.price}`;
    if (order.type === 'Sell' && currentPrice >= order.price) return `Цена ${currentPrice} >= лимит продажи ${order.price}`;
    return null;
  };

  // ── Transactions ────────────────────────────────────────────
  const openNewTxModal = () => {
    setEditingTx(null);
    txForm.resetFields();
   txForm.setFieldsValue({ type: 'Deposit', createdAt: dayjs() });
    setTxModalOpen(true);
  };
  const openEditTxModal = (tx: Transaction) => {
    setEditingTx(tx);
    txForm.setFieldsValue({
      type: tx.type,
      amount: tx.amount,
     createdAt: dayjs.utc(tx.createdAt).local(),
     stockId: tx.stockId ?? undefined,
     description: tx.description ?? undefined,
   });
   setTxModalOpen(true);
  };
  const handleTxSubmit = async (values: { type: TransactionType; amount: number; createdAt: Dayjs; stockId?: number; description?: string }) => {
   if (!id) return;
   setTxSubmitting(true);
   try {
     const payload = {
       type: values.type,
       amount: Math.abs(values.amount),
       createdAt: values.createdAt.toISOString(),
       stockId: values.stockId ?? null,
       description: values.description,
     };
     if (editingTx) {
       await updateTransaction(Number(id), editingTx.id, payload);
       message.success('Транзакция обновлена');
      } else {
        await createTransaction(Number(id), payload);
        message.success('Транзакция добавлена');
      }
      setTxModalOpen(false); txForm.resetFields(); fetchFinanceData();
    } catch { message.error('Ошибка сохранения транзакции'); }
    finally { setTxSubmitting(false); }
  };
  const startBalanceEdit = (field: BalanceField) => {
    if (!balance) return;
    setBalanceDraft({
      cashBalance: balance.cashBalance,
    });
    setBalanceEditField(field);
  };
  const cancelBalanceEdit = () => {
    if (balance) {
      setBalanceDraft({
        cashBalance: balance.cashBalance,
      });
    }
    setBalanceEditField(null);
  };
  const handleBalanceDraftChange = (field: BalanceField, value: number | null) => {
    setBalanceDraft((current) => ({
      ...current,
      [field]: value ?? 0,
    }));
  };
  const handleBalanceSave = async () => {
    if (!id || !balanceEditField) return;
    if (!Number.isFinite(balanceDraft.cashBalance)) {
      message.error('Введите корректное число');
      return;
    }

    setBalanceSubmitting(true);
    try {
      await updateBalance(Number(id), {
        cashBalance: balanceDraft.cashBalance,
      });
      message.success('Баланс обновлён');
      setBalanceEditField(null);
      await fetchFinanceData();
    } catch {
      message.error('Ошибка сохранения баланса');
    } finally {
      setBalanceSubmitting(false);
    }
  };
  const handleDeleteTx = async (txId: number) => {
    if (!id) return;
    try { await deleteTransaction(Number(id), txId); message.success('Транзакция удалена'); fetchFinanceData(); }
    catch { message.error('Ошибка удаления транзакции'); }
  };

  // ── Summary ────────────────────────────────────────────────
  const computeSummary = (items: PortfolioItem[]) => {
    const totalValue = items.reduce((sum, item) => sum + item.stock.currentPrice * item.quantity, 0);
    const totalCost = items.reduce((sum, item) => sum + item.buyPrice * item.quantity, 0);
    const totalPnlEur = totalValue - totalCost;
    const totalPnlPct = totalCost > 0 ? (totalPnlEur / totalCost) * 100 : 0;
    return { totalValue, totalPnlEur, totalPnlPct, count: items.length };
  };

  const items = portfolio?.items ?? [];

  const sortedItems = useMemo(() => {
    return [...items].sort((a, b) => {
      const nameA = a.stock?.name ?? '';
      const nameB = b.stock?.name ?? '';
      if (!nameA && nameB) return 1;
      if (nameA && !nameB) return -1;
      const cmp = nameA.localeCompare(nameB, 'ru', { sensitivity: 'base' });
      if (cmp !== 0) return cmp;
      const tickerCmp = (a.stock?.ticker ?? '').localeCompare(b.stock?.ticker ?? '', 'ru', { sensitivity: 'base' });
      if (tickerCmp !== 0) return tickerCmp;
      return a.id - b.id;
    });
  }, [items]);

  const summary = computeSummary(items);

  const pendingOrders = orders.filter((o) => o.status === 'Pending');
  const executedOrders = orders.filter((o) => o.status === 'Executed' || o.status === 'Cancelled');

  const filteredTransactions = useMemo(() => {
    if (txTypeFilter === 'all') return transactions;
    return transactions.filter((t) => t.type === txTypeFilter);
  }, [transactions, txTypeFilter]);
  const stockOptions = useMemo(
    () => stocks.map((s) => ({ value: s.id, label: `${s.ticker} — ${s.name}` })),
    [stocks],
  );
  const previewStocksValue = balance?.stocksValue ?? 0;
  const previewTotalPortfolioValue = previewStocksValue + balanceDraft.cashBalance;

  const renderBalanceRow = (field: BalanceField, label: string) => {
    const isEditing = balanceEditField === field;

    return (
      <div
        key={field}
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          gap: 12,
        }}
      >
        <div>
          <Text type="secondary">{label}</Text>
          {!isEditing && <div><Text strong style={{ fontSize: 18 }}>{formatCurrency(balanceDraft[field])}</Text></div>}
        </div>
        {isEditing ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <InputNumber
              value={balanceDraft[field]}
              onChange={(value) => handleBalanceDraftChange(field, value)}
              step={0.01}
              precision={2}
              size="small"
              style={{ width: 140 }}
            />
            <Button
              type="text"
              size="small"
              icon={<CheckOutlined />}
              loading={balanceSubmitting}
              aria-label={`Сохранить ${label.toLowerCase()}`}
              onClick={handleBalanceSave}
            />
            <Button
              type="text"
              size="small"
              icon={<CloseOutlined />}
              disabled={balanceSubmitting}
              aria-label={`Отменить редактирование ${label.toLowerCase()}`}
              onClick={cancelBalanceEdit}
            />
          </div>
        ) : (
          <Button
            type="text"
            size="small"
            icon={<EditOutlined />}
            aria-label={`Редактировать ${label.toLowerCase()}`}
            onClick={() => startBalanceEdit(field)}
          />
        )}
      </div>
    );
  };

  // ── Columns ────────────────────────────────────────────────
  const positionColumns = [
    {
      title: 'Тикер',
      key: 'ticker',
      render: (_: unknown, record: PositionTableRow) => {
        if (isPositionChartRow(record)) {
          const item = items.find((i) => i.id === record._itemId);
          return {
            children: (
              <StockPriceChart
                panelId={`pos-chart-panel-${record._itemId}`}
                stockId={record._stockId}
                ticker={item?.stock?.ticker ?? ''}
                name={item?.stock?.name ?? ''}
                wkn={item?.stock?.wkn ?? null}
                isin={item?.stock?.isin ?? null}
                storedPriceEur={item?.stock?.currentPrice ?? null}
              />
            ),
            props: { colSpan: TOTAL_POS_COLS },
          };
        }
        const item = record as PortfolioItem;
        const ticker = item.stock?.ticker;
        if (!ticker) return <Tag color="blue">—</Tag>;
        const isExpanded = expandedPositionId === item.id;
        return (
          <button
            type="button"
            onClick={() => setExpandedPositionId((prev) => (prev === item.id ? null : item.id))}
            aria-expanded={isExpanded}
            aria-controls={`pos-chart-panel-${item.id}`}
            aria-label={isExpanded ? `Закрыть график цены: ${ticker}` : `Открыть график цены: ${ticker}`}
            style={{
              padding: 0, background: 'none', border: 'none', cursor: 'pointer',
              fontWeight: 600, color: isExpanded ? '#1677ff' : 'inherit',
              display: 'inline-flex', alignItems: 'center', gap: 4,
            }}
          >
            <CaretRightFilled
              style={{
                fontSize: 10, transition: 'transform 0.2s',
                transform: isExpanded ? 'rotate(90deg)' : 'rotate(0deg)',
                color: '#1677ff',
              }}
            />
            {ticker}
          </button>
        );
      },
    },
    {
      title: 'Название', key: 'name',
      render: (_: unknown, record: PositionTableRow) => {
        if (isPositionChartRow(record)) return { children: null, props: { colSpan: 0 } };
        return (record as PortfolioItem).stock?.name ?? '—';
      },
    },
    {
      title: 'Кол-во', key: 'quantity',
      render: (_: unknown, record: PositionTableRow) => {
        if (isPositionChartRow(record)) return { children: null, props: { colSpan: 0 } };
        return (record as PortfolioItem).quantity.toFixed(2);
      },
    },
    {
      title: 'Цена покупки', key: 'buyPrice',
      render: (_: unknown, record: PositionTableRow) => {
        if (isPositionChartRow(record)) return { children: null, props: { colSpan: 0 } };
        return `€${(record as PortfolioItem).buyPrice.toFixed(2)}`;
      },
    },
    {
      title: 'Тек. цена', key: 'currentPrice',
      render: (_: unknown, record: PositionTableRow) => {
        if (isPositionChartRow(record)) return { children: null, props: { colSpan: 0 } };
        return `€${(record as PortfolioItem).stock.currentPrice.toFixed(2)}`;
      },
    },
    {
      title: 'Тек. стоимость', key: 'currentValue',
      render: (_: unknown, record: PositionTableRow) => {
        if (isPositionChartRow(record)) return { children: null, props: { colSpan: 0 } };
        const r = record as PortfolioItem;
        return `€${(r.stock.currentPrice * r.quantity).toFixed(2)}`;
      },
    },
    {
      title: 'P&L (€)', key: 'pnlEur',
      render: (_: unknown, record: PositionTableRow) => {
        if (isPositionChartRow(record)) return { children: null, props: { colSpan: 0 } };
        const r = record as PortfolioItem;
        const pnl = (r.stock.currentPrice - r.buyPrice) * r.quantity;
        return <span style={{ color: pnl >= 0 ? '#3f8600' : '#cf1322' }}>{pnl >= 0 ? '+' : ''}€{pnl.toFixed(2)}</span>;
      },
    },
    {
      title: 'P&L (%)', key: 'pnlPct',
      render: (_: unknown, record: PositionTableRow) => {
        if (isPositionChartRow(record)) return { children: null, props: { colSpan: 0 } };
        const r = record as PortfolioItem;
        const pct = r.buyPrice > 0 ? ((r.stock.currentPrice - r.buyPrice) / r.buyPrice) * 100 : 0;
        return <span style={{ color: pct >= 0 ? '#3f8600' : '#cf1322' }}>{pct >= 0 ? '+' : ''}{pct.toFixed(2)}%</span>;
      },
    },
    {
      title: 'Действия', key: 'actions',
      render: (_: unknown, record: PositionTableRow) => {
        if (isPositionChartRow(record)) return { children: null, props: { colSpan: 0 } };
        const r = record as PortfolioItem;
        return (
          <div style={{ display: 'flex', gap: 6 }}>
            <Tooltip title="Изменить">
              <Button icon={<EditOutlined />} size="small" aria-label="Изменить" onClick={() => openEditPosModal(r)} />
            </Tooltip>
            <Popconfirm title="Удалить позицию?" onConfirm={() => handleDeleteItem(r.id)} okText="Да" cancelText="Нет">
              <Tooltip title="Удалить">
                <Button icon={<DeleteOutlined />} size="small" danger aria-label="Удалить" />
              </Tooltip>
            </Popconfirm>
          </div>
        );
      },
    },
  ];

  const pendingOrderColumns = [
    {
      title: '', key: 'alert', width: 32,
      render: (_: unknown, r: Order) => {
        const msg = isTriggered(r);
        return msg ? <Tooltip title={msg}><BellOutlined style={{ color: '#faad14', fontSize: 16 }} /></Tooltip> : null;
      },
    },
    { title: 'Тикер', key: 'ticker', render: (_: unknown, r: Order) => <Tag color="blue">{r.stock?.ticker ?? '—'}</Tag> },
    { title: 'Название', key: 'name', render: (_: unknown, r: Order) => r.stock?.name ?? '—' },
    { title: 'Тип', dataIndex: 'type', key: 'type', render: (v: OrderType) => <Tag color={ORDER_TYPE_COLORS[v]}>{ORDER_TYPE_LABELS[v]}</Tag> },
    { title: 'Кол-во', dataIndex: 'quantity', key: 'quantity', render: (v: number) => v.toFixed(2) },
    { title: 'Цена', dataIndex: 'price', key: 'price', render: (v: number) => `€${v.toFixed(2)}` },
    { title: 'Stop Loss', dataIndex: 'stopLoss', key: 'stopLoss', render: (v: number | null) => v != null ? `€${v.toFixed(2)}` : '—' },
    { title: 'Stop Market', dataIndex: 'stopMarket', key: 'stopMarket', render: (v: number | null) => v != null ? `€${v.toFixed(2)}` : '—' },
    { title: 'Тек. цена', key: 'currentPrice', render: (_: unknown, r: Order) => `€${r.stock?.currentPrice?.toFixed(2) ?? '—'}` },
    { title: 'Создан', dataIndex: 'createdAt', key: 'createdAt', render: (v: string) => dayjs.utc(v).local().format('DD.MM.YYYY HH:mm') },
    {
      title: 'Действия', key: 'actions',
      render: (_: unknown, r: Order) => (
        <div style={{ display: 'flex', gap: 8 }}>
          <Button icon={<EditOutlined />} size="small" onClick={() => openEditOrderModal(r)}>Изменить</Button>
          <Popconfirm title="Удалить ордер?" onConfirm={() => handleDeleteOrder(r.id)} okText="Да" cancelText="Нет">
            <Button icon={<DeleteOutlined />} size="small" danger>Удалить</Button>
          </Popconfirm>
        </div>
      ),
    },
  ];

  const executedOrderColumns = [
    { title: 'Дата исполнения', dataIndex: 'executedAt', key: 'executedAt', render: (v: string | null) => v ? dayjs.utc(v).local().format('DD.MM.YYYY') : '—' },
    { title: 'Тикер', key: 'ticker', render: (_: unknown, r: Order) => <Tag color="blue">{r.stock?.ticker ?? '—'}</Tag> },
    { title: 'Название', key: 'name', render: (_: unknown, r: Order) => r.stock?.name ?? '—' },
    { title: 'Тип', dataIndex: 'type', key: 'type', render: (v: OrderType) => <Tag color={ORDER_TYPE_COLORS[v]}>{ORDER_TYPE_LABELS[v]}</Tag> },
    { title: 'Статус', dataIndex: 'status', key: 'status', render: (v: OrderStatus) => <Tag color={ORDER_STATUS_COLORS[v]}>{ORDER_STATUS_LABELS[v]}</Tag> },
    { title: 'Кол-во', dataIndex: 'quantity', key: 'quantity', render: (v: number) => v.toFixed(2) },
    { title: 'Цена', dataIndex: 'price', key: 'price', render: (v: number) => `€${v.toFixed(2)}` },
    { title: 'Итого', key: 'total', render: (_: unknown, r: Order) => `€${(r.price * r.quantity).toFixed(2)}` },
    {
      title: 'Удалить', key: 'delete',
      render: (_: unknown, r: Order) => (
        <Popconfirm title="Удалить ордер?" onConfirm={() => handleDeleteOrder(r.id)} okText="Да" cancelText="Нет">
          <Button icon={<DeleteOutlined />} size="small" danger>Удалить</Button>
        </Popconfirm>
      ),
    },
  ];

  // ── Derived keys ───────────────────────────────────────────
  const sectionKey = `portfolio-${id}-${section}`;
  const sidebarOpenKeys = ['portfolios', `portfolio-${id}`];

  // Position table data with inline chart rows
  const positionTableData: PositionTableRow[] = useMemo(() => {
    const rows: PositionTableRow[] = [];
    for (const item of sortedItems) {
      rows.push(item);
      if (expandedPositionId === item.id) {
        rows.push({ _isPositionChartRow: true, _itemId: item.id, _stockId: item.stockId });
      }
    }
    return rows;
  }, [sortedItems, expandedPositionId]);

  return (
    <>
      <AuthenticatedShell
        portfolios={portfolios}
        selectedKeys={[sectionKey]}
        userName={user?.username}
        onLogout={logout}
        defaultOpenKeys={sidebarOpenKeys}
        activePortfolioId={id}
        headerLeft={(
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/')}>Назад</Button>
            <Title level={4} style={{ margin: 0 }}>{portfolio?.name ?? 'Портфель'}</Title>
          </div>
        )}
      >
          {loading ? (
            <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}><Spin size="large" /></div>
          ) : (
            <>
              {section === 'positions' && (
                <Row gutter={[16, 16]} style={{ marginBottom: 24 }}>
                  <Col xs={24} sm={12} lg={6}>
                    <Card><Text type="secondary">Общая стоимость</Text><Title level={4} style={{ margin: 0 }}>{formatCurrency(summary.totalValue)}</Title></Card>
                  </Col>
                  <Col xs={24} sm={12} lg={6}>
                    <Card>
                      <Text type="secondary">Общий P&L (€)</Text>
                      <Title level={4} style={{ margin: 0, color: summary.totalPnlEur >= 0 ? '#3f8600' : '#cf1322' }}>
                        {summary.totalPnlEur >= 0 ? '+' : ''}{formatCurrency(summary.totalPnlEur)}
                      </Title>
                    </Card>
                  </Col>
                  <Col xs={24} sm={12} lg={6}>
                    <Card>
                      <Text type="secondary">Общий P&L (%)</Text>
                      <Title level={4} style={{ margin: 0, color: summary.totalPnlPct >= 0 ? '#3f8600' : '#cf1322' }}>
                        {summary.totalPnlPct >= 0 ? '+' : ''}{summary.totalPnlPct.toFixed(2)}%
                      </Title>
                    </Card>
                  </Col>
                  <Col xs={24} sm={12} lg={6}>
                    <Card><Text type="secondary">Позиций</Text><Title level={4} style={{ margin: 0 }}>{summary.count}</Title></Card>
                  </Col>
                </Row>
              )}

              {/* ── Positions (with Orders as tab) ─────────────── */}
              {section === 'positions' && (
                <Tabs
                  defaultActiveKey="positions"
                  tabBarExtraContent={
                    <div style={{ display: 'flex', gap: 8 }}>
                      <Button type="primary" icon={<PlusOutlined />} onClick={openAddPosModal}>
                        Добавить позицию
                      </Button>
                      <Button icon={<PlusOutlined />} onClick={openAddOrderModal}>
                        Создать ордер
                      </Button>
                    </div>
                  }
                  items={[
                    {
                      key: 'positions',
                      label: 'Позиции',
                      children: (
                        <Table
                          className="positions-table"
                          dataSource={positionTableData}
                          columns={positionColumns}
                          rowKey={(record: PositionTableRow) =>
                            isPositionChartRow(record)
                              ? `pos-chart-${record._itemId}`
                              : String((record as PortfolioItem).id)
                          }
                          rowClassName={(record: PositionTableRow) =>
                            isPositionChartRow(record) ? 'chart-panel-row' : ''
                          }
                          scroll={{ x: true }}
                          pagination={{ pageSize: 20 }}
                        />
                      ),
                    },
                    {
                      key: 'orders',
                      label: (
                        <span>
                          Ордера
                          {pendingOrders.length > 0 && (
                            <Tag color="gold" style={{ marginLeft: 6 }}>{pendingOrders.length}</Tag>
                          )}
                        </span>
                      ),
                      children: (
                        <Tabs
                          defaultActiveKey="pending"
                          items={[
                            {
                              key: 'pending',
                              label: (
                                <span>
                                  Ожидающие
                                  {pendingOrders.length > 0 && (
                                    <Tag color="gold" style={{ marginLeft: 6 }}>{pendingOrders.length}</Tag>
                                  )}
                                </span>
                              ),
                              children: (
                                <Table
                                  dataSource={pendingOrders}
                                  columns={pendingOrderColumns}
                                  rowKey="id"
                                  scroll={{ x: true }}
                                  pagination={{ pageSize: 20 }}
                                  locale={{ emptyText: 'Нет ожидающих ордеров' }}
                                />
                              ),
                            },
                            {
                              key: 'executed',
                              label: (
                                <span>
                                  Выполненные
                                  {executedOrders.length > 0 && (
                                    <Tag color="green" style={{ marginLeft: 6 }}>{executedOrders.length}</Tag>
                                  )}
                                </span>
                              ),
                              children: (
                                <Table
                                  dataSource={executedOrders}
                                  columns={executedOrderColumns}
                                  rowKey="id"
                                  scroll={{ x: true }}
                                  pagination={{ pageSize: 20 }}
                                  locale={{ emptyText: 'Нет выполненных ордеров' }}
                                />
                              ),
                            },
                          ]}
                        />
                      ),
                    },
                  ]}
                />
              )}

              {/* ── Transactions ──────────────────────────────────── */}
              {section === 'transactions' && (
                financeLoaded ? (
                  <>
                    {/* Balance summary */}
                    {balance && (
                      <Row gutter={[16, 16]} style={{ marginBottom: 24 }}>
                        <Col xs={24} sm={8}>
                          <Card>
                            <Text type="secondary">Итого портфель</Text>
                            <Title level={4} style={{ margin: 0 }}>
                              {formatCurrency(balanceEditField ? previewTotalPortfolioValue : balance.totalPortfolioValue)}
                            </Title>
                          </Card>
                        </Col>
                        <Col xs={24} sm={8}>
                          <Card>
                            <Text type="secondary">Стоимость акций</Text>
                            <Title level={4} style={{ margin: 0 }}>{formatCurrency(balance.stocksValue)}</Title>
                          </Card>
                        </Col>
                        <Col xs={24} sm={8}>
                          <Card>
                            {renderBalanceRow('cashBalance', 'Денежный баланс')}
                          </Card>
                        </Col>
                      </Row>
                    )}

                    {/* Toolbar */}
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16, flexWrap: 'wrap', gap: 8 }}>
                      <Select
                        value={txTypeFilter}
                        onChange={(v) => setTxTypeFilter(v)}
                        style={{ minWidth: 180 }}
                        options={[
                          { value: 'all', label: 'Все типы' },
                          { value: 'Deposit', label: 'Пополнение' },
                          { value: 'Withdrawal', label: 'Вывод' },
                          { value: 'Buy', label: 'Покупка' },
                          { value: 'Sell', label: 'Продажа' },
                          { value: 'Dividend', label: 'Дивиденды' },
                        ]}
                      />
                      <Button type="primary" icon={<PlusOutlined />} onClick={openNewTxModal}>
                        Новая транзакция
                      </Button>
                    </div>

                    {/* Transaction journal */}
                    <Table
                      dataSource={filteredTransactions}
                      rowKey="id"
                      scroll={{ x: true }}
                      pagination={{ pageSize: 20 }}
                      columns={[
                        {
                          title: 'Дата', dataIndex: 'createdAt', key: 'createdAt',
                          render: (v: string) => dayjs.utc(v).local().format('DD.MM.YYYY HH:mm'),
                          sorter: (a: Transaction, b: Transaction) =>
                            dayjs(a.createdAt).valueOf() - dayjs(b.createdAt).valueOf(),
                          defaultSortOrder: 'descend',
                        },
                        {
                          title: 'Тип', dataIndex: 'type', key: 'type',
                          render: (v: TransactionType) => (
                            <Tag color={TX_TYPE_COLORS[v]}>{TX_TYPE_LABELS[v]}</Tag>
                          ),
                        },
                        {
                          title: 'Сумма', key: 'amount',
                          render: (_: unknown, t: Transaction) => {
                            const signed = getEffectiveSignedAmount(t);
                            const color = signed >= 0 ? '#3f8600' : '#cf1322';
                            return <span style={{ color }}>{signed >= 0 ? '+' : ''}€{Math.abs(signed).toFixed(2)}</span>;
                          },
                        },
                        {
                          title: 'Описание', key: 'description',
                          render: (_: unknown, t: Transaction) => getTransactionDescription(t),
                        },
                        {
                          title: 'Действия', key: 'actions',
                          render: (_: unknown, t: Transaction) => (
                            <div style={{ display: 'flex', gap: 6 }}>
                              {!t.orderId && (
                                <Tooltip title="Редактировать">
                                  <Button
                                    icon={<EditOutlined />}
                                    size="small"
                                    aria-label="Редактировать транзакцию"
                                    onClick={() => openEditTxModal(t)}
                                  />
                                </Tooltip>
                              )}
                              <Popconfirm
                                title="Удалить транзакцию?"
                                onConfirm={() => handleDeleteTx(t.id)}
                                okText="Да"
                                cancelText="Нет"
                              >
                                <Tooltip title="Удалить">
                                  <Button
                                    icon={<DeleteOutlined />}
                                    size="small"
                                    danger
                                    aria-label="Удалить транзакцию"
                                  />
                                </Tooltip>
                              </Popconfirm>
                            </div>
                          ),
                        },
                      ]}
                    />
                  </>
                ) : (
                  <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}><Spin /></div>
                )
              )}
            </>
          )}
      </AuthenticatedShell>

      {/* Position Modal */}
      <Modal
        title={editingItem ? 'Редактировать позицию' : 'Добавить позицию'}
        open={posModalOpen}
        onCancel={() => { setPosModalOpen(false); posForm.resetFields(); setEditingItem(null); }}
        footer={null}
      >
        <Form form={posForm} layout="vertical" onFinish={handlePosSubmit}>
          <Form.Item label="Акция" name="stockId" rules={[{ required: true, message: 'Выберите акцию' }]}>
            <Select placeholder="Выберите акцию" showSearch optionFilterProp="children" disabled={!!editingItem}>
              {stocks.map((s) => <Select.Option key={s.id} value={s.id}>{s.ticker} — {s.name}</Select.Option>)}
            </Select>
          </Form.Item>
          <Form.Item label="Количество" name="quantity" rules={[{ required: true, message: 'Введите количество' }]}>
            <InputNumber min={0.01} step={0.01} style={{ width: '100%' }} placeholder="Количество" />
          </Form.Item>
          <Form.Item label="Цена покупки (€)" name="buyPrice" rules={[{ required: true, message: 'Введите цену покупки' }]}>
            <InputNumber min={0} step={0.01} style={{ width: '100%' }} placeholder="Цена покупки" prefix="€" />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={posSubmitting} block>
              {editingItem ? 'Сохранить' : 'Добавить'}
            </Button>
          </Form.Item>
        </Form>
      </Modal>

      {/* Order Modal */}
      <Modal
        title={editingOrder ? 'Редактировать ордер' : 'Создать ордер'}
        open={orderModalOpen}
        onCancel={() => { setOrderModalOpen(false); orderForm.resetFields(); setEditingOrder(null); }}
        footer={null}
        width={520}
      >
        <Form form={orderForm} layout="vertical" onFinish={handleOrderSubmit}>
          <Form.Item label="Акция" name="stockId" rules={[{ required: true, message: 'Выберите акцию' }]}>
            <Select placeholder="Выберите акцию" showSearch optionFilterProp="children" disabled={!!editingOrder}>
              {stocks.map((s) => <Select.Option key={s.id} value={s.id}>{s.ticker} — {s.name}</Select.Option>)}
            </Select>
          </Form.Item>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item label="Тип" name="type" rules={[{ required: true, message: 'Выберите тип' }]}>
                <Select placeholder="Тип">
                  <Select.Option value="Buy">Покупка</Select.Option>
                  <Select.Option value="Sell">Продажа</Select.Option>
                </Select>
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item label="Статус" name="status" initialValue="Pending">
                <Select>
                  <Select.Option value="Pending">Ожидание</Select.Option>
                  <Select.Option value="Executed">Выполнено</Select.Option>
                  <Select.Option value="Cancelled">Отменено</Select.Option>
                </Select>
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item label="Количество" name="quantity" rules={[{ required: true, message: 'Введите количество' }]}>
                <InputNumber min={0.01} step={0.01} style={{ width: '100%' }} placeholder="Количество" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item label="Цена (€)" name="price" rules={[{ required: true, message: 'Введите цену' }]}>
                <InputNumber min={0} step={0.01} style={{ width: '100%' }} prefix="€" />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item label="Stop Loss (€)" name="stopLoss">
                <InputNumber min={0} step={0.01} style={{ width: '100%' }} prefix="€" placeholder="Опционально" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item label="Stop Market (€)" name="stopMarket">
                <InputNumber min={0} step={0.01} style={{ width: '100%' }} prefix="€" placeholder="Опционально" />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={orderSubmitting} block>
              {editingOrder ? 'Сохранить' : 'Создать'}
            </Button>
          </Form.Item>
        </Form>
      </Modal>

      {/* Transaction Modal */}
      <Modal
        title={editingTx ? 'Редактировать транзакцию' : 'Новая транзакция'}
        open={txModalOpen}
        onCancel={() => { setTxModalOpen(false); txForm.resetFields(); setEditingTx(null); }}
        footer={null}
      >
        <Form form={txForm} layout="vertical" onFinish={handleTxSubmit} initialValues={{ type: 'Deposit', createdAt: dayjs() }}>
          <Form.Item label="Тип" name="type" rules={[{ required: true, message: 'Выберите тип' }]}>
            <Select>
              <Select.Option value="Deposit">Пополнение</Select.Option>
              <Select.Option value="Withdrawal">Вывод</Select.Option>
              <Select.Option value="Buy">Покупка</Select.Option>
              <Select.Option value="Sell">Продажа</Select.Option>
              <Select.Option value="Dividend">Дивиденды</Select.Option>
            </Select>
          </Form.Item>
          <Form.Item label="Сумма (€)" name="amount" rules={[{ required: true, message: 'Введите сумму' }]}>
            <InputNumber min={0.01} step={0.01} precision={2} style={{ width: '100%' }} prefix="€" placeholder="Положительная сумма" />
          </Form.Item>
          <Form.Item label="Дата" name="createdAt" rules={[{ required: true, message: 'Выберите дату и время' }]}>
            <DatePicker
              showTime={{ format: 'HH:mm' }}
              format="DD.MM.YYYY HH:mm"
              style={{ width: '100%' }}
              placeholder="Выберите дату и время"
            />
          </Form.Item>
          <Form.Item label="Акция" name="stockId">
            <Select
              allowClear
              placeholder="Необязательно"
              showSearch
              optionFilterProp="label"
              options={stockOptions}
            />
          </Form.Item>
          <Form.Item label="Описание" name="description">
            <Input placeholder="Необязательно" />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={txSubmitting} block>
              {editingTx ? 'Сохранить' : 'Добавить'}
            </Button>
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};

export default PortfolioDetailPage;
