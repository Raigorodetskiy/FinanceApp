import React, { useState, useEffect } from 'react';
import {
  Layout,
  Menu,
  Table,
  Button,
  Modal,
  Form,
  InputNumber,
  Select,
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
  DatePicker,
} from 'antd';
import {
  DashboardOutlined,
  FolderOutlined,
  StockOutlined,
  UserOutlined,
  LogoutOutlined,
  PlusOutlined,
  ArrowLeftOutlined,
  EditOutlined,
  DeleteOutlined,
  BellOutlined,
  WalletOutlined,
} from '@ant-design/icons';
import { useNavigate, useParams } from 'react-router-dom';
import dayjs from 'dayjs';
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
  getTransactions,
  createTransaction,
  deleteTransaction,
  getDividends,
  createDividend,
  deleteDividend,
} from '../services/api';
import { useAuth } from '../contexts/AuthContext';
import type {
  Portfolio,
  Stock,
  PortfolioItem,
  Order,
  OrderType,
  OrderStatus,
  Transaction,
  Dividend,
  PortfolioBalance,
} from '../types';

const { Sider, Header, Content } = Layout;
const { Title, Text } = Typography;

const ORDER_TYPE_LABELS: Record<OrderType, string> = { Buy: 'Покупка', Sell: 'Продажа' };
const ORDER_STATUS_LABELS: Record<OrderStatus, string> = { Pending: 'Ожидание', Executed: 'Выполнено', Cancelled: 'Отменено' };
const ORDER_STATUS_COLORS: Record<OrderStatus, string> = { Pending: 'gold', Executed: 'green', Cancelled: 'red' };
const ORDER_TYPE_COLORS: Record<OrderType, string> = { Buy: 'blue', Sell: 'volcano' };

const PortfolioDetailPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const [portfolio, setPortfolio] = useState<Portfolio | null>(null);
  const [stocks, setStocks] = useState<Stock[]>([]);
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);

  // Finance state
  const [balance, setBalance] = useState<PortfolioBalance | null>(null);
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [dividends, setDividends] = useState<Dividend[]>([]);
  const [financeLoaded, setFinanceLoaded] = useState(false);

  // Transaction modal
  const [txModalOpen, setTxModalOpen] = useState(false);
  const [txType, setTxType] = useState<'Deposit' | 'Withdrawal'>('Deposit');
  const [txSubmitting, setTxSubmitting] = useState(false);
  const [txForm] = Form.useForm();

  // Dividend modal
  const [divModalOpen, setDivModalOpen] = useState(false);
  const [divSubmitting, setDivSubmitting] = useState(false);
  const [divForm] = Form.useForm();

  // Position modal
  const [posModalOpen, setPosModalOpen] = useState(false);
  const [editingItem, setEditingItem] = useState<PortfolioItem | null>(null);
  const [posSubmitting, setPosSubmitting] = useState(false);
  const [posForm] = Form.useForm();

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

  useEffect(() => { fetchData(); }, [id]);

  const fetchFinanceData = async () => {
    if (!id) return;
    try {
      const [balanceRes, txRes, divRes] = await Promise.all([
        getBalance(Number(id)),
        getTransactions(Number(id)),
        getDividends(Number(id)),
      ]);
      setBalance(balanceRes.data);
      setTransactions(txRes.data);
      setDividends(divRes.data);
      setFinanceLoaded(true);
    } catch {
      message.error('Ошибка загрузки финансовых данных');
    }
  };

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
    } catch { message.error('Ошибка сохранения ордера'); }
    finally { setOrderSubmitting(false); }
  };
  const handleDeleteOrder = async (orderId: number) => {
    if (!id) return;
    try { await deleteOrder(Number(id), orderId); message.success('Ордер удалён'); fetchData(); }
    catch { message.error('Ошибка удаления ордера'); }
  };

  // Indicator: order should have triggered based on current price
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

  // ── Finance handlers ───────────────────────────────────────
  const openDepositModal = () => { setTxType('Deposit'); txForm.resetFields(); setTxModalOpen(true); };
  const openWithdrawModal = () => { setTxType('Withdrawal'); txForm.resetFields(); setTxModalOpen(true); };
  const handleTxSubmit = async (values: { amount: number; description?: string }) => {
    if (!id) return;
    setTxSubmitting(true);
    try {
      await createTransaction(Number(id), { type: txType, amount: values.amount, description: values.description });
      message.success(txType === 'Deposit' ? 'Пополнение добавлено' : 'Вывод добавлен');
      setTxModalOpen(false); txForm.resetFields(); fetchFinanceData();
    } catch { message.error('Ошибка сохранения транзакции'); }
    finally { setTxSubmitting(false); }
  };
  const handleDeleteTx = async (txId: number) => {
    if (!id) return;
    try { await deleteTransaction(Number(id), txId); message.success('Транзакция удалена'); fetchFinanceData(); }
    catch { message.error('Ошибка удаления транзакции'); }
  };

  const openAddDivModal = () => { divForm.resetFields(); setDivModalOpen(true); };
  const handleDivSubmit = async (values: { stockId: number; amount: number; paidAt: dayjs.Dayjs }) => {
    if (!id) return;
    setDivSubmitting(true);
    try {
      await createDividend(Number(id), { stockId: values.stockId, amount: values.amount, paidAt: values.paidAt.toISOString() });
      message.success('Дивиденд добавлен');
      setDivModalOpen(false); divForm.resetFields(); fetchFinanceData();
    } catch { message.error('Ошибка добавления дивиденда'); }
    finally { setDivSubmitting(false); }
  };
  const handleDeleteDiv = async (divId: number) => {
    if (!id) return;
    try { await deleteDividend(Number(id), divId); message.success('Дивиденд удалён'); fetchFinanceData(); }
    catch { message.error('Ошибка удаления дивиденда'); }
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
  const summary = computeSummary(items);

  // ── Derived order lists ───────────────────────────���────────
  const pendingOrders = orders.filter((o) => o.status === 'Pending');
  const executedOrders = orders.filter((o) => o.status === 'Executed' || o.status === 'Cancelled');
  const triggeredCount = pendingOrders.filter((o) => isTriggered(o)).length;

  // ── Columns ────────────────────────────────────────────────
  const positionColumns = [
    { title: 'Тикер', dataIndex: ['stock', 'ticker'], key: 'ticker', render: (t: string) => <Tag color="blue">{t}</Tag> },
    { title: 'Название', dataIndex: ['stock', 'name'], key: 'name' },
    { title: 'Кол-во', dataIndex: 'quantity', key: 'quantity', render: (v: number) => v.toFixed(2) },
    { title: 'Цена покупки', dataIndex: 'buyPrice', key: 'buyPrice', render: (v: number) => `€${v.toFixed(2)}` },
    { title: 'Тек. цена', key: 'currentPrice', render: (_: unknown, r: PortfolioItem) => `€${r.stock.currentPrice.toFixed(2)}` },
    { title: 'Тек. стоимость', key: 'currentValue', render: (_: unknown, r: PortfolioItem) => `€${(r.stock.currentPrice * r.quantity).toFixed(2)}` },
    {
      title: 'P&L (€)', key: 'pnlEur',
      render: (_: unknown, r: PortfolioItem) => {
        const pnl = (r.stock.currentPrice - r.buyPrice) * r.quantity;
        return <span style={{ color: pnl >= 0 ? '#3f8600' : '#cf1322' }}>{pnl >= 0 ? '+' : ''}€{pnl.toFixed(2)}</span>;
      },
    },
    {
      title: 'P&L (%)', key: 'pnlPct',
      render: (_: unknown, r: PortfolioItem) => {
        const pct = r.buyPrice > 0 ? ((r.stock.currentPrice - r.buyPrice) / r.buyPrice) * 100 : 0;
        return <span style={{ color: pct >= 0 ? '#3f8600' : '#cf1322' }}>{pct >= 0 ? '+' : ''}{pct.toFixed(2)}%</span>;
      },
    },
    {
      title: 'Действия', key: 'actions',
      render: (_: unknown, r: PortfolioItem) => (
        <div style={{ display: 'flex', gap: 8 }}>
          <Button icon={<EditOutlined />} size="small" onClick={() => openEditPosModal(r)}>Изменить</Button>
          <Popconfirm title="Удалить позицию?" onConfirm={() => handleDeleteItem(r.id)} okText="Да" cancelText="Нет">
            <Button icon={<DeleteOutlined />} size="small" danger>Удалить</Button>
          </Popconfirm>
        </div>
      ),
    },
  ];

  // Pending orders — full details + alerts
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

  // Executed/Cancelled orders — compact view
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

  const menuItems = [
    { key: 'dashboard', icon: <DashboardOutlined />, label: 'Dashboard', onClick: () => navigate('/') },
    {
      key: 'portfolios', icon: <FolderOutlined />, label: 'Мои портфели',
      children: portfolios.map((p) => ({ key: `portfolio-${p.id}`, label: p.name, onClick: () => navigate(`/portfolios/${p.id}`) })),
    },
    { key: 'stocks', icon: <StockOutlined />, label: 'Акции', onClick: () => navigate('/stocks') },
    { key: 'profile', icon: <UserOutlined />, label: user?.username ?? 'Профиль' },
    { key: 'logout', icon: <LogoutOutlined />, label: 'Выйти', onClick: logout, danger: true },
  ];

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider collapsible breakpoint="lg" collapsedWidth="0">
        <div style={{ color: '#fff', padding: '16px', fontSize: 18, fontWeight: 700 }}>💹 FinanceApp</div>
        <Menu theme="dark" mode="inline" defaultOpenKeys={['portfolios']} selectedKeys={[`portfolio-${id}`]} items={menuItems} />
      </Sider>
      <Layout>
        <Header style={{ background: '#fff', padding: '0 24px', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/')}>Назад</Button>
            <Title level={4} style={{ margin: 0 }}>{portfolio?.name ?? 'Портфель'}</Title>
          </div>
        </Header>
        <Content style={{ padding: 24 }}>
          {loading ? (
            <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}><Spin size="large" /></div>
          ) : (
            <>
              <Row gutter={[16, 16]} style={{ marginBottom: 24 }}>
                <Col xs={24} sm={12} lg={6}>
                  <Card><Text type="secondary">Общая стоимость</Text><Title level={4} style={{ margin: 0 }}>€{summary.totalValue.toFixed(2)}</Title></Card>
                </Col>
                <Col xs={24} sm={12} lg={6}>
                  <Card>
                    <Text type="secondary">Общий P&L (€)</Text>
                    <Title level={4} style={{ margin: 0, color: summary.totalPnlEur >= 0 ? '#3f8600' : '#cf1322' }}>
                      {summary.totalPnlEur >= 0 ? '+' : ''}€{summary.totalPnlEur.toFixed(2)}
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

              <Tabs
                defaultActiveKey="positions"
                onChange={(key) => { if (key === 'finance' && !financeLoaded) fetchFinanceData(); }}
                items={[
                  {
                    key: 'positions',
                    label: 'Позиции',
                    children: (
                      <>
                        <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 16 }}>
                          <Button type="primary" icon={<PlusOutlined />} onClick={openAddPosModal}>Добавить позицию</Button>
                        </div>
                        <Table dataSource={items} columns={positionColumns} rowKey="id" scroll={{ x: true }} pagination={{ pageSize: 20 }} />
                      </>
                    ),
                  },
                  {
                    key: 'orders',
                    label: (
                      <span>
                        Ордера
                        {triggeredCount > 0 && (
                          <Tag color="orange" style={{ marginLeft: 6 }}>{triggeredCount}</Tag>
                        )}
                      </span>
                    ),
                    children: (
                      <>
                        <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 16 }}>
                          <Button type="primary" icon={<PlusOutlined />} onClick={openAddOrderModal}>Создать ордер</Button>
                        </div>
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
                      </>
                    ),
                  },
                  {
                    key: 'finance',
                    label: <span><WalletOutlined style={{ marginRight: 4 }} />Финансы</span>,
                    children: (
                      <Tabs
                        defaultActiveKey="balance"
                        items={[
                          {
                            key: 'balance',
                            label: 'Баланс',
                            children: balance ? (
                              <>
                                <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
                                  <Col xs={24} sm={12} lg={8}>
                                    <Card><Text type="secondary">Денежный баланс</Text><Title level={4} style={{ margin: 0 }}>€{balance.cashBalance.toFixed(2)}</Title></Card>
                                  </Col>
                                  <Col xs={24} sm={12} lg={8}>
                                    <Card><Text type="secondary">Кредит брокера</Text><Title level={4} style={{ margin: 0 }}>€{balance.brokerCredit.toFixed(2)}</Title></Card>
                                  </Col>
                                  <Col xs={24} sm={12} lg={8}>
                                    <Card><Text type="secondary">Общий баланс</Text><Title level={4} style={{ margin: 0 }}>€{balance.totalBalance.toFixed(2)}</Title></Card>
                                  </Col>
                                  <Col xs={24} sm={12} lg={8}>
                                    <Card><Text type="secondary">Стоимость акций</Text><Title level={4} style={{ margin: 0 }}>€{balance.stocksValue.toFixed(2)}</Title></Card>
                                  </Col>
                                  <Col xs={24} sm={12} lg={8}>
                                    <Card><Text type="secondary">Итого портфель</Text><Title level={4} style={{ margin: 0 }}>€{balance.totalPortfolioValue.toFixed(2)}</Title></Card>
                                  </Col>
                                </Row>
                                <div style={{ display: 'flex', gap: 8 }}>
                                  <Button type="primary" icon={<PlusOutlined />} onClick={openDepositModal}>Пополнить</Button>
                                  <Button icon={<PlusOutlined />} onClick={openWithdrawModal}>Вывести</Button>
                                </div>
                              </>
                            ) : (
                              <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}><Spin /></div>
                            ),
                          },
                          {
                            key: 'transactions',
                            label: 'Транзакции',
                            children: (
                              <>
                                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginBottom: 16 }}>
                                  <Button type="primary" icon={<PlusOutlined />} onClick={openDepositModal}>Пополнить</Button>
                                  <Button icon={<PlusOutlined />} onClick={openWithdrawModal}>Вывести</Button>
                                </div>
                                <Table
                                  dataSource={transactions}
                                  rowKey="id"
                                  scroll={{ x: true }}
                                  pagination={{ pageSize: 20 }}
                                  columns={[
                                    { title: 'Дата', dataIndex: 'createdAt', key: 'createdAt', render: (v: string) => dayjs.utc(v).local().format('DD.MM.YYYY HH:mm') },
                                    {
                                      title: 'Тип', dataIndex: 'type', key: 'type',
                                      render: (v: string) => <Tag color={v === 'Deposit' ? 'green' : 'red'}>{v === 'Deposit' ? 'Пополнение' : 'Вывод'}</Tag>,
                                    },
                                    { title: 'Сумма', dataIndex: 'amount', key: 'amount', render: (v: number) => `€${v.toFixed(2)}` },
                                    { title: 'Описание', dataIndex: 'description', key: 'description', render: (v: string | null) => v ?? '—' },
                                    {
                                      title: 'Удалить', key: 'delete',
                                      render: (_: unknown, r: Transaction) => (
                                        <Popconfirm title="Удалить транзакцию?" onConfirm={() => handleDeleteTx(r.id)} okText="Да" cancelText="Нет">
                                          <Button icon={<DeleteOutlined />} size="small" danger>Удалить</Button>
                                        </Popconfirm>
                                      ),
                                    },
                                  ]}
                                />
                              </>
                            ),
                          },
                          {
                            key: 'dividends',
                            label: 'Дивиденды',
                            children: (
                              <>
                                <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 16 }}>
                                  <Button type="primary" icon={<PlusOutlined />} onClick={openAddDivModal}>Добавить дивиденд</Button>
                                </div>
                                <Table
                                  dataSource={dividends}
                                  rowKey="id"
                                  scroll={{ x: true }}
                                  pagination={{ pageSize: 20 }}
                                  columns={[
                                    { title: 'Дата выплаты', dataIndex: 'paidAt', key: 'paidAt', render: (v: string) => dayjs.utc(v).local().format('DD.MM.YYYY') },
                                    { title: 'Тикер', key: 'ticker', render: (_: unknown, r: Dividend) => <Tag color="blue">{r.stock?.ticker ?? '—'}</Tag> },
                                    { title: 'Название', key: 'name', render: (_: unknown, r: Dividend) => r.stock?.name ?? '—' },
                                    { title: 'Сумма', dataIndex: 'amount', key: 'amount', render: (v: number) => `€${v.toFixed(2)}` },
                                    {
                                      title: 'Удалить', key: 'delete',
                                      render: (_: unknown, r: Dividend) => (
                                        <Popconfirm title="Удалить дивиденд?" onConfirm={() => handleDeleteDiv(r.id)} okText="Да" cancelText="Нет">
                                          <Button icon={<DeleteOutlined />} size="small" danger>Удалить</Button>
                                        </Popconfirm>
                                      ),
                                    },
                                  ]}
                                />
                              </>
                            ),
                          },
                        ]}
                      />
                    ),
                  },
                ]}
              />
            </>
          )}
        </Content>
      </Layout>

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
        title={txType === 'Deposit' ? 'Пополнить баланс' : 'Вывести средства'}
        open={txModalOpen}
        onCancel={() => { setTxModalOpen(false); txForm.resetFields(); }}
        footer={null}
      >
        <Form form={txForm} layout="vertical" onFinish={handleTxSubmit}>
          <Form.Item label="Сумма (€)" name="amount" rules={[{ required: true, message: 'Введите сумму' }]}>
            <InputNumber min={0.01} step={0.01} style={{ width: '100%' }} prefix="€" />
          </Form.Item>
          <Form.Item label="Описание" name="description">
            <Input placeholder="Необязательно" />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={txSubmitting} block>
              {txType === 'Deposit' ? 'Пополнить' : 'Вывести'}
            </Button>
          </Form.Item>
        </Form>
      </Modal>

      {/* Dividend Modal */}
      <Modal
        title="Добавить дивиденд"
        open={divModalOpen}
        onCancel={() => { setDivModalOpen(false); divForm.resetFields(); }}
        footer={null}
      >
        <Form form={divForm} layout="vertical" onFinish={handleDivSubmit}>
          <Form.Item label="Акция" name="stockId" rules={[{ required: true, message: 'Выберите акцию' }]}>
            <Select placeholder="Выберите акцию" showSearch optionFilterProp="children">
              {stocks.map((s) => <Select.Option key={s.id} value={s.id}>{s.ticker} — {s.name}</Select.Option>)}
            </Select>
          </Form.Item>
          <Form.Item label="Сумма (€)" name="amount" rules={[{ required: true, message: 'Введите сумму' }]}>
            <InputNumber min={0.01} step={0.01} style={{ width: '100%' }} prefix="€" />
          </Form.Item>
          <Form.Item label="Дата выплаты" name="paidAt" rules={[{ required: true, message: 'Укажите дату выплаты' }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={divSubmitting} block>Добавить</Button>
          </Form.Item>
        </Form>
      </Modal>
    </Layout>
  );
};

export default PortfolioDetailPage;
