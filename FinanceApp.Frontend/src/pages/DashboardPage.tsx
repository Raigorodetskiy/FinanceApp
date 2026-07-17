import React, { useState, useEffect } from 'react';
import {
  Layout,
  Menu,
  Button,
  Card,
  Row,
  Col,
  Modal,
  Form,
  Input,
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
  DeleteOutlined,
  FolderOpenOutlined,
} from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import dayjs from 'dayjs';
import {
  getPortfolios,
  createPortfolio,
  deletePortfolio,
} from '../services/api';
import { useAuth } from '../contexts/AuthContext';
import type { Portfolio } from '../types';

const { Sider, Header, Content } = Layout;
const { Title, Text } = Typography;

const DashboardPage: React.FC = () => {
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [form] = Form.useForm();
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const fetchPortfolios = async () => {
    setLoading(true);
    try {
      const res = await getPortfolios();
      setPortfolios(res.data);
    } catch {
      message.error('Ошибка загрузки портфелей');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchPortfolios();
  }, []);

  const handleCreate = async (values: { name: string }) => {
    setCreating(true);
    try {
      await createPortfolio(values);
      message.success('Портфель создан');
      setModalOpen(false);
      form.resetFields();
      fetchPortfolios();
    } catch {
      message.error('Ошибка создания портфеля');
    } finally {
      setCreating(false);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await deletePortfolio(id);
      message.success('Портфель удалён');
      fetchPortfolios();
    } catch {
      message.error('Ошибка удаления портфеля');
    }
  };

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
          defaultSelectedKeys={['dashboard']}
          items={menuItems}
        />
      </Sider>
      <Layout>
        <Header style={{ background: '#fff', padding: '0 24px', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <Title level={4} style={{ margin: 0 }}>
            Dashboard
          </Title>
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => setModalOpen(true)}
          >
            Создать портфель
          </Button>
        </Header>
        <Content style={{ padding: 24 }}>
          {loading ? (
            <div style={{ display: 'flex', justifyContent: 'center', padding: 48 }}>
              <Spin size="large" />
            </div>
          ) : (
            <Row gutter={[16, 16]}>
              {portfolios.map((portfolio) => (
                <Col xs={24} sm={12} lg={8} key={portfolio.id}>
                  <Card
                    title={portfolio.name}
                    actions={[
                      <Button
                        key="open"
                        type="link"
                        icon={<FolderOpenOutlined />}
                        onClick={() => navigate(`/portfolios/${portfolio.id}`)}
                      >
                        Открыть
                      </Button>,
                      <Popconfirm
                        key="delete"
                        title="Удалить портфель?"
                        onConfirm={() => handleDelete(portfolio.id)}
                        okText="Да"
                        cancelText="Нет"
                      >
                        <Button type="link" danger icon={<DeleteOutlined />}>
                          Удалить
                        </Button>
                      </Popconfirm>,
                    ]}
                  >
                    <Text type="secondary">
                      Позиций: {portfolio.items?.length ?? 0}
                    </Text>
                    <br />
                    <Text type="secondary">
                      Создан: {dayjs(portfolio.createdAt).format('DD.MM.YYYY')}
                    </Text>
                  </Card>
                </Col>
              ))}
              {portfolios.length === 0 && (
                <Col span={24}>
                  <Card>
                    <Text type="secondary">
                      Портфелей пока нет. Создайте первый!
                    </Text>
                  </Card>
                </Col>
              )}
            </Row>
          )}
        </Content>
      </Layout>

      <Modal
        title="Создать портфель"
        open={modalOpen}
        onCancel={() => { setModalOpen(false); form.resetFields(); }}
        footer={null}
      >
        <Form form={form} layout="vertical" onFinish={handleCreate}>
          <Form.Item
            label="Название"
            name="name"
            rules={[{ required: true, message: 'Введите название портфеля' }]}
          >
            <Input placeholder="Мой портфель" />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={creating} block>
              Создать
            </Button>
          </Form.Item>
        </Form>
      </Modal>
    </Layout>
  );
};

export default DashboardPage;
