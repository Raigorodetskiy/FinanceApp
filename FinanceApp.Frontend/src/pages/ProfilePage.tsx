import React, { useEffect, useState } from 'react';
import { Alert, Button, Card, Col, Form, Input, Row, Typography, message } from 'antd';
import AuthenticatedShell from '../components/AuthenticatedShell';
import { useAuth } from '../contexts/AuthContext';
import { changeMyPassword, getPortfolios, updateMyProfile } from '../services/api';
import type { Portfolio } from '../types';

const { Title, Text } = Typography;
export const PROFILE_ROUTE_KEY = 'profile';
export const PROFILE_PASSWORD_LOGOUT_MESSAGE = 'После смены пароля вы будете автоматически разлогинены.';

const ProfilePage: React.FC = () => {
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [profileForm] = Form.useForm<{ username: string; currentPassword: string }>();
  const [passwordForm] = Form.useForm<{ currentPassword: string; newPassword: string; confirmPassword: string }>();
  const [savingProfile, setSavingProfile] = useState(false);
  const [savingPassword, setSavingPassword] = useState(false);
  const { user, logout, refreshUser } = useAuth();

  useEffect(() => {
    getPortfolios()
      .then((res) => setPortfolios(res.data))
      .catch(() => setPortfolios([]));
  }, []);

  useEffect(() => {
    if (!user) {
      return;
    }

    profileForm.setFieldsValue({ username: user.username, currentPassword: '' });
  }, [user, profileForm]);

  const handleProfileSubmit = async (values: { username: string; currentPassword: string }) => {
    setSavingProfile(true);
    try {
      await updateMyProfile(values);
      await refreshUser();
      profileForm.setFieldsValue({ currentPassword: '' });
      message.success('Логин обновлён');
    } catch (error: any) {
      const errorText = typeof error?.response?.data === 'string' ? error.response.data : 'Не удалось обновить профиль';
      message.error(errorText);
    } finally {
      setSavingProfile(false);
    }
  };

  const handlePasswordSubmit = async (values: { currentPassword: string; newPassword: string; confirmPassword: string }) => {
    setSavingPassword(true);
    try {
      await changeMyPassword(values);
      message.success('Пароль изменён. Выполните вход снова.');
      passwordForm.resetFields();
      logout();
    } catch (error: any) {
      const errorText = typeof error?.response?.data === 'string' ? error.response.data : 'Не удалось изменить пароль';
      message.error(errorText);
    } finally {
      setSavingPassword(false);
    }
  };

  return (
    <AuthenticatedShell
      portfolios={portfolios}
      selectedKeys={[PROFILE_ROUTE_KEY]}
      userName={user?.username}
      onLogout={logout}
      headerLeft={<Title level={4} style={{ margin: 0 }}>Профиль</Title>}
    >
      <Row gutter={[16, 16]}>
        <Col xs={24} lg={12}>
          <Card title="Данные учётной записи">
            <Text type="secondary">Email: {user?.email ?? '—'}</Text>
            <Form
              form={profileForm}
              layout="vertical"
              style={{ marginTop: 16 }}
              onFinish={handleProfileSubmit}
              autoComplete="off"
            >
              <Form.Item
                name="username"
                label="Логин"
                rules={[
                  { required: true, message: 'Введите логин' },
                  { min: 3, max: 32, message: 'Логин должен содержать от 3 до 32 символов' },
                  { pattern: /^[A-Za-z0-9._-]+$/, message: 'Допустимы буквы, цифры и символы . _ -' },
                  { pattern: /^[^@]+$/, message: 'Символ @ не допускается' },
                ]}
              >
                <Input maxLength={32} />
              </Form.Item>
              <Form.Item
                name="currentPassword"
                label="Текущий пароль"
                rules={[{ required: true, message: 'Введите текущий пароль' }]}
              >
                <Input.Password autoComplete="current-password" />
              </Form.Item>
              <Button type="primary" htmlType="submit" loading={savingProfile}>Сохранить логин</Button>
            </Form>
          </Card>
        </Col>

        <Col xs={24} lg={12}>
          <Card title="Смена пароля">
            <Alert
              type="info"
              showIcon
              message={PROFILE_PASSWORD_LOGOUT_MESSAGE}
              style={{ marginBottom: 16 }}
            />
            <Form
              form={passwordForm}
              layout="vertical"
              onFinish={handlePasswordSubmit}
              autoComplete="off"
            >
              <Form.Item
                name="currentPassword"
                label="Текущий пароль"
                rules={[{ required: true, message: 'Введите текущий пароль' }]}
              >
                <Input.Password autoComplete="current-password" />
              </Form.Item>
              <Form.Item
                name="newPassword"
                label="Новый пароль"
                rules={[
                  { required: true, message: 'Введите новый пароль' },
                  { min: 8, message: 'Минимум 8 символов' },
                ]}
              >
                <Input.Password autoComplete="new-password" />
              </Form.Item>
              <Form.Item
                name="confirmPassword"
                dependencies={['newPassword']}
                label="Подтверждение нового пароля"
                rules={[
                  { required: true, message: 'Подтвердите новый пароль' },
                  ({ getFieldValue }) => ({
                    validator(_, value) {
                      if (!value || getFieldValue('newPassword') === value) {
                        return Promise.resolve();
                      }

                      return Promise.reject(new Error('Пароли не совпадают'));
                    },
                  }),
                ]}
              >
                <Input.Password autoComplete="new-password" />
              </Form.Item>
              <Button type="primary" htmlType="submit" loading={savingPassword}>Сменить пароль</Button>
            </Form>
          </Card>
        </Col>
      </Row>
    </AuthenticatedShell>
  );
};

export default ProfilePage;
