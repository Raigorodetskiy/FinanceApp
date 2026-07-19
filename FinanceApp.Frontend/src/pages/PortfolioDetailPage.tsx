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
  Popconfirm,
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
} from '@ant-design/icons';
import { useNavigate, useParams } from 'react-router-dom';
import {
  getPortfolio,
  getStocks,
  addPortfolioItem,
  updatePortfolioItem,
  deletePortfolioItem,
  getPortfolios,
} from '../services/api';
import { useAuth } from '../contexts/AuthContext';
import type { Portfolio, Stock, PortfolioItem } from '../types';

const { Sider, Header, Content } = Layout;
const { Title, Text } = Typography;

const PortfolioDetailPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const [portfolio, setPortfolio] = useState<Portfolio | null>(null);
  const [stocks, setStocks] = useState<Stock[]>([]);
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingItem, setEditingItem] = useState<PortfolioItem | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [form] = Form.useForm();
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const fetchData = async () => {
    if (!id) return;
    setLoading(true);
    try {
      const [portfolioRes, stocksRes, portfoliosRes] = await Promise.all([
        getPortfolio(Number(id)),
        getStocks(),
        getPortfolios(),
      ]);
      setPortfolio(portfolioRes.data);
      setStocks(stocksRes.data);
      setPortfolios(portfoliosRes.data);
    } catch {
      message.error('Ошибка загрузки данных');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchData();
  }, [id]);

  const openAddModal = () => {
    setEditingItem(null);
    form.resetFields();
    setModalOpen(true);
  };

  const openEditModal = (item: PortfolioItem) => {
    setEditingItem(item);
    form.setFieldsValue({
      stockId: item.stockId,
      quantity: item.quantity,
      buyPrice: item.buyPrice,
    });
    setModalOpen(true);
  };

  const handleSubmit = async (values: {
    stockId: number;
    quantity: number;
    buyPrice: number;
  }) => {
    if (!id) return;
    setSubmitting(true);
    try {
      if (editingItem) {
        await updatePortfolioItem(Number(id), editingItem.id, values);
        message.success('Позиция обновлена');
      } else {
        await addPortfolioItem(Number(id), values);
        message.success('Позиция добавлена');
      }
      setModalOpen(false);
      form.resetFields();
      fetchData();
    } catch {
      message.error('Ошибка сохранения позиции');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDeleteItem = async (itemId: number) => {
    if (!id) return;
    try {
      await deletePortfolioItem(Number(id), itemId);
      message.success('Позиция удалена');
      fetchData();
    } catch {
      message.error('Ошибка удаления позиции');
    }
  };

  const computeSummary = (items: PortfolioItem[]) => {
    const totalValue = items.reduce(
      (sum, item) => sum + item.stock.currentPrice * item.quantity,
      0
    );
    const totalCost = items.reduce(
      (sum, item) => sum + item.buyPrice * item.quantity,
      0
    );
    const totalPnlEur = totalValue - totalCost;
    const totalPnlPct = totalCost > 0 ? (totalPnlEur / totalCost) * 100 : 0;
    return { totalValue, totalPnlEur, totalPnlPct, count: items.length };
  };

  const columns = [
    {
      title: 'Тикер',
      dataIndex: ['stock', 'ticker'],
      key: 'ticker',
      render: (ticker: string) => <Tag color="blue">{ticker}</Tag>,
    },
    {
      title: 'Название',
      dataIndex: ['stock', 'name'],
      key: 'name',
    },
    {
      title: 'Кол-во',
      dataIndex: 'quantity',
      key: 'quantity',
      render: (v: number) => v.toFixed(2),
    },
    {
      title: 'Цена покупки',
      dataIndex: 'buyPrice',
      key: 'buyPrice',
      render: (v: number) => `€${v.toFixed(2)}`,
    },
    {
      title: 'Тек. цена',
      key: 'currentPrice',
      render: (_: unknown, record: PortfolioItem) =>
        `€${record.stock.currentPrice.toFixed(2)}`,
    },
    {
      title: 'Тек. стоимость',
      key: 'currentValue',
      render: (_: unknown, record: PortfolioItem) =>
        `€${(record.stock.currentPrice * record.quantity).toFixed(2)}`,
    },
    {
      title: 'P&L (€)',
      key: 'pnlEur',
      render: (_: unknown, record: PortfolioItem) => {
        const pnl =
          (record.stock.currentPrice - record.buyPrice) * record.quantity;
        return (
          <span style={{ color: pnl >= 0 ? '#3f8600' : '#cf1322' }}>
            {pnl >= 0 ? '+' : ''}€{pnl.toFixed(2)}
          </span>
        );
      },
    },
    {
      title: 'P&L (%)',
      key: 'pnlPct',
      render: (_: unknown, record: PortfolioItem) => {
        const pnlPct =
          record.buyPrice > 0
            ? ((record.stock.currentPrice - record.buyPrice) /
                record.buyPrice) *
              100
            : 0;
        return (
          <span style={{ color: pnlPct >= 0 ? '#3f8600' : '#cf1322' }}>
            {pnlPct >= 0 ? '+' : ''}
            {pnlPct.toFixed(2)}%
          </span>
        );
      },
    },
    {
      title: 'Действия',
      key: 'actions',
      render: (_: unknown, record: PortfolioItem) => (
        <div style={{ display: 'flex', gap: 8 }}>
          <Button
            icon={<EditOutlined />}
            size="small"
            onClick={() => openEditModal(record)}
          >
            Изменить
          </Button>
          <Popconfirm
            title="Удалить позицию?"
            onConfirm={() => handleDeleteItem(record.id)}
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

  const items = portfolio?.items ?? [];
  const summary = computeSummary(items);

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider collapsible breakpoint="lg" collapsedWidth="0">
        <div style={{ color: '#fff', padding: '16px', fontSize: 18, fontWeight: 700 }}>
          💹 FinanceApp
        </div>
        <Menu
          theme="dark"
          mode="inline"
          defaultOpenKeys={['portfolios']}
          selectedKeys={[`portfolio-${id}`]}
          items={menuItems}
        />
      </Sider>
      <Layout>
        <Header style={{ background: '#fff', padding: '0 24px', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/')}>
              Назад
            </Button>
            <Title level={4} style={{ margin: 0 }}>
              {portfolio?.name ?? 'Портфель'}
            </Title>
          </div>
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={openAddModal}
          >
            Добавить позицию
          </Button>
        </Header>
        <Content style={{ padding: 24 }}>
          {loading ? (
            <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}>
              <Spin size="large" />
            </div>
          ) : (
            <>
              <Row gutter={[16, 16]} style={{ marginBottom: 24 }}>
                <Col xs={24} sm={12} lg={6}>
                  <Card>
                    <Text type="secondary">Общая стоимость</Text>
                    <Title level={4} style={{ margin: 0 }}>
                      €{summary.totalValue.toFixed(2)}
                    </Title>
                  </Card>
                </Col>
                <Col xs={24} sm={12} lg={6}>
                  <Card>
                    <Text type="secondary">Общий P&L (€)</Text>
                    <Title
                      level={4}
                      style={{ margin: 0, color: summary.totalPnlEur >= 0 ? '#3f8600' : '#cf1322' }}
                    >
                      {summary.totalPnlEur >= 0 ? '+' : ''}€{summary.totalPnlEur.toFixed(2)}
                    </Title>
                  </Card>
                </Col>
                <Col xs={24} sm={12} lg={6}>
                  <Card>
                    <Text type="secondary">Общий P&L (%)</Text>
                    <Title
                      level={4}
                      style={{ margin: 0, color: summary.totalPnlPct >= 0 ? '#3f8600' : '#cf1322' }}
                    >
                      {summary.totalPnlPct >= 0 ? '+' : ''}{summary.totalPnlPct.toFixed(2)}%
                    </Title>
                  </Card>
                </Col>
                <Col xs={24} sm={12} lg={6}>
                  <Card>
                    <Text type="secondary">Позиций</Text>
                    <Title level={4} style={{ margin: 0 }}>
                      {summary.count}
                    </Title>
                  </Card>
                </Col>
              </Row>

              <Table
                dataSource={items}
                columns={columns}
                rowKey="id"
                scroll={{ x: true }}
                pagination={{ pageSize: 20 }}
              />
            </>
          )}
        </Content>
      </Layout>

      <Modal
        title={editingItem ? 'Редактировать позицию' : 'Добавить позицию'}
        open={modalOpen}
        onCancel={() => { setModalOpen(false); form.resetFields(); setEditingItem(null); }}
        footer={null}
      >
        <Form form={form} layout="vertical" onFinish={handleSubmit}>
          <Form.Item
            label="Акция"
            name="stockId"
            rules={[{ required: true, message: 'Выберите акцию' }]}
          >
            <Select
              placeholder="Выберите акцию"
              showSearch
              optionFilterProp="children"
              disabled={!!editingItem}
            >
              {stocks.map((stock) => (
                <Select.Option key={stock.id} value={stock.id}>
                  {stock.ticker} — {stock.name}
                </Select.Option>
              ))}
            </Select>
          </Form.Item>
          <Form.Item
            label="Количество"
            name="quantity"
            rules={[{ required: true, message: 'Введите количество' }]}
          >
            <InputNumber
              min={0.01}
              step={0.01}
              style={{ width: '100%' }}
              placeholder="Количество"
            />
          </Form.Item>
          <Form.Item
            label="Цена покупки (€)"
            name="buyPrice"
            rules={[{ required: true, message: 'Введите цену покупки' }]}
          >
            <InputNumber
              min={0}
              step={0.01}
              style={{ width: '100%' }}
              placeholder="Цена покупки"
              prefix="€"
            />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={submitting} block>
              {editingItem ? 'Сохранить' : 'Добавить'}
            </Button>
          </Form.Item>
        </Form>
      </Modal>
    </Layout>
  );
};

export default PortfolioDetailPage;
