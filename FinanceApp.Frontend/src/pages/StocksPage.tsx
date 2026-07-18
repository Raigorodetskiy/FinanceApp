import React, { useState, useEffect } from 'react';
import {
  Layout,
  Menu,
  Table,
  Button,
  Modal,
  Form,
  Input,
  InputNumber,
  Spin,
  Typography,
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
} from '../services/api';
import { useAuth } from '../contexts/AuthContext';
import type { Stock, Portfolio } from '../types';

const { Sider, Header, Content } = Layout;
const { Title } = Typography;

const StocksPage: React.FC = () => {
  const [stocks, setStocks] = useState<Stock[]>([]);
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingStock, setEditingStock] = useState<Stock | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [livePrices, setLivePrices] = useState<Record<number, { price: number | null; priceEur: number | null; loading: boolean }>>({});
  const [form] = Form.useForm();
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const fetchData = async () => {
    setLoading(true);
    try {
      const [stocksRes, portfoliosRes] = await Promise.all([
        getStocks(),
        getPortfolios(),
      ]);
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
  }, []);

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
    exchange: string;
    currentPrice: number;
  }) => {
    setSubmitting(true);
    try {
      if (editingStock) {
        await updateStock(editingStock.id, {
          ...editingStock,
          ...values,
          updatedAt: new Date().toISOString(),
        });
        message.success('Акция обновлена');
      } else {
        await createStock(values);
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

  const handleRefreshPrices = async () => {
    setRefreshing(true);
    try {
      const stocksWithTicker = stocks.filter((s) => s.ticker?.trim());
      const results = await Promise.allSettled(
        stocksWithTicker.map(async (stock) => {
          const priceRes = await getStockPrice(stock.ticker);
          await updateStock(stock.id, {
            ...stock,
            currentPrice: priceRes.data.currentPrice,
            updatedAt: new Date().toISOString(),
          });
        })
      );
      const failed = results.filter((r) => r.status === 'rejected').length;
      await fetchData();
      if (failed === 0) {
        message.success('Цены обновлены');
      } else {
        message.warning(`Цены обновлены частично (${failed} ошибок)`);
      }
    } catch {
      message.error('Ошибка обновления цен');
    } finally {
      setRefreshing(false);
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
      // EUR/USD rate means 1 EUR = eurUsd USD, so priceEur = priceUsd / eurUsd
      const priceEur = eurUsd > 0 ? priceUsd / eurUsd : priceUsd;

      setLivePrices((prev) => ({
        ...prev,
        [stock.id]: { price: priceUsd, priceEur, loading: false },
      }));

      // Save converted EUR price to DB
      await updateStock(stock.id, {
        ...stock,
        currentPrice: Math.round(priceEur * 100) / 100,
        updatedAt: new Date().toISOString(),
      });

      // Update local stocks state so Текущая цена column refreshes
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
    },
    {
      title: 'Название',
      dataIndex: 'name',
      key: 'name',
    },
    {
      title: 'Биржа',
      dataIndex: 'exchange',
      key: 'exchange',
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
        return (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <span>
              {live?.loading
                ? '...'
                : live?.price != null
                ? `$${live.price.toFixed(2)} USD`
                : '—'}
            </span>
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
          <div style={{ display: 'flex', gap: 8 }}>
            <Button
              icon={<ReloadOutlined />}
              loading={refreshing}
              onClick={handleRefreshPrices}
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
            <Table
              dataSource={stocks}
              columns={columns}
              rowKey="id"
              scroll={{ x: true }}
              pagination={{ pageSize: 20 }}
            />
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
            label="Биржа"
            name="exchange"
            rules={[{ required: true, message: 'Введите биржу' }]}
          >
            <Input placeholder="NASDAQ" />
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
