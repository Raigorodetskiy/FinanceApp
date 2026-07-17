import React, { useState } from 'react';
import { Form, Input, Button, Card, Typography, message } from 'antd';
import { Link, useNavigate } from 'react-router-dom';
import { register as registerApi } from '../services/api';

const { Title } = Typography;

const RegisterPage: React.FC = () => {
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();

  const onFinish = async (values: {
    username: string;
    email: string;
    password: string;
    confirmPassword: string;
  }) => {
    if (values.password !== values.confirmPassword) {
      message.error('Пароли не совпадают');
      return;
    }
    setLoading(true);
    try {
      await registerApi({
        username: values.username,
        email: values.email,
        password: values.password,
      });
      message.success('Регистрация прошла успешно');
      navigate('/login');
    } catch {
      message.error('Ошибка регистрации. Попробуйте снова.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div
      style={{
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'center',
        minHeight: '100vh',
        background: '#f0f2f5',
      }}
    >
      <Card style={{ width: 400 }}>
        <Title level={2} style={{ textAlign: 'center', marginBottom: 24 }}>
          Регистрация
        </Title>
        <Form layout="vertical" onFinish={onFinish}>
          <Form.Item
            label="Имя пользователя"
            name="username"
            rules={[{ required: true, message: 'Введите имя пользователя' }]}
          >
            <Input placeholder="Username" />
          </Form.Item>
          <Form.Item
            label="Email"
            name="email"
            rules={[{ required: true, message: 'Введите email' }, { type: 'email', message: 'Некорректный email' }]}
          >
            <Input placeholder="Email" />
          </Form.Item>
          <Form.Item
            label="Пароль"
            name="password"
            rules={[{ required: true, message: 'Введите пароль' }, { min: 6, message: 'Минимум 6 символов' }]}
          >
            <Input.Password placeholder="Пароль" />
          </Form.Item>
          <Form.Item
            label="Подтвердите пароль"
            name="confirmPassword"
            rules={[{ required: true, message: 'Подтвердите пароль' }]}
          >
            <Input.Password placeholder="Подтвердите пароль" />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={loading} block>
              Зарегистрироваться
            </Button>
          </Form.Item>
        </Form>
        <div style={{ textAlign: 'center' }}>
          Уже есть аккаунт? <Link to="/login">Войти</Link>
        </div>
      </Card>
    </div>
  );
};

export default RegisterPage;
