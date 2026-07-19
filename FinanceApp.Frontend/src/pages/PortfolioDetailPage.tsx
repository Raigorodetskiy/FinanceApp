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
} from '../services/api';
import { useAuth } from '../contexts/AuthContext';
import type { Portfolio, Stock, PortfolioItem, Order, OrderType, OrderStatus } from '../types';

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
    // Limit check
    if (order.type === 'Buy' && currentPrice <= order.price) return `Цена ${currentPrice} <= лимит покупки ${order.price}`;
    if (order.type === 'Sell' && currentPrice >= order.price) return `Цена ${currentPrice} >= лимит продажи ${order.price}`;
    return null;
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

  const orderColumns = [
    {
      title: '', key: 'alert', width: 32,
      render: (_: unknown, r: Order) => {
        const msg = isTriggered(r);
        return msg ? <Tooltip title={msg}><BellOutlined style={{ color: '#faad14', fontSize: 16 }} /></Tooltip> : null;
      },
    },
    { title: 'Тикер', dataIndex: ['stock', 'ticker'], key: 'ticker', render: (t: string) => <Tag color="blue">{t}</Tag> },
    { title: 'Тип', dataIndex: 'type', key: 'type', render: (v: OrderType) => <Tag color={ORDER_TYPE_COLORS[v]}>{ORDER_TYPE_LABELS[v]}</Tag> },
    { title: 'Статус', dataIndex: 'status', key: 'status', render: (v: OrderStatus) => <Tag color={ORDER_STATUS_COLORS[v]}>{ORDER_STATUS_LABELS[v]}</Tag> },
    { title: 'Кол-во', dataIndex: 'quantity', key: 'quantity', render: (v: number) => v.toFixed(2) },
    { title: 'Цена', dataIndex: 'price', key: 'price', render: (v: number) => `€${v.toFixed(2)}` },
    { title: 'Stop Loss', dataIndex: 'stopLoss', key: 'stopLoss', render: (v: number | null) => v != null ? `€${v.toFixed(2)}` : '—' },
    { title: 'Stop Market', dataIndex: 'stopMarket', key: 'stopMarket', render: (v: number | null) => v != null ? `€${v.toFixed(2)}` : '—' },
    { title: 'Тек. цена', key: 'currentPrice', render: (_: unknown, r: Order) => `€${r.stock?.currentPrice?.toFixed(2) ?? '—'}` },
    { title: 'Создан', dataIndex: 'createdAt', key: 'createdAt', render: (v: string) => dayjs(v).format('DD.MM.YYYY HH:mm') },
    { title: 'Исполнен', dataIndex: 'executedAt', key: 'executedAt', render: (v: string | null) => v ? dayjs(v).format('DD.MM.YYYY HH:mm') : '—' },
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
                        {orders.filter(o => isTriggered(o)).length > 0 && (
                          <Tag color="orange" style={{ marginLeft: 6 }}>{orders.filter(o => isTriggered(o)).length}</Tag>
                        )}
                      </span>
                    ),
                    children: (
                      <>
                        <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 16 }}>
                          <Button type="primary" icon={<PlusOutlined />} onClick={openAddOrderModal}>Создать ордер</Button>
                        </div>
                        <Table dataSource={orders} columns={orderColumns} rowKey="id" scroll={{ x: true }} pagination={{ pageSize: 20 }} />
                      </>
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
    </Layout>
  );
};

export default PortfolioDetailPage;
