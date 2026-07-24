import React from 'react';
import { DashboardOutlined, FolderOutlined, LogoutOutlined, StockOutlined, UserOutlined } from '@ant-design/icons';
import { Layout, Menu } from 'antd';
import type { MenuProps } from 'antd';
import { useNavigate } from 'react-router-dom';
import type { Portfolio } from '../types';
import './AppSidebar.css';

const { Sider } = Layout;
const PORTFOLIO_KEY_PREFIX = 'portfolio-';

interface AppSidebarProps {
  portfolios: Portfolio[];
  selectedKeys: string[];
  userName?: string;
  onLogout: () => void;
  defaultOpenKeys?: string[];
}

const resolveDefaultOpenKeys = (selectedKeys: string[], defaultOpenKeys?: string[]) => (
  defaultOpenKeys ?? (
    selectedKeys.some((key) => key.startsWith(PORTFOLIO_KEY_PREFIX)) ? ['portfolios'] : []
  )
);

const AppSidebar: React.FC<AppSidebarProps> = ({
  portfolios,
  selectedKeys,
  userName,
  onLogout,
  defaultOpenKeys,
}) => {
  const navigate = useNavigate();
  const hasPortfolios = portfolios.length > 0;
  const portfolioRootClassName = hasPortfolios
    ? 'portfolio-tree-root portfolio-tree-root--populated'
    : 'portfolio-tree-root';
  const resolvedDefaultOpenKeys = resolveDefaultOpenKeys(selectedKeys, defaultOpenKeys);

  const menuItems: MenuProps['items'] = [
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
      className: portfolioRootClassName,
      children: portfolios.map((portfolio) => ({
        key: `${PORTFOLIO_KEY_PREFIX}${portfolio.id}`,
        className: 'portfolio-tree-node',
        label: (
          <span className="portfolio-tree-node-label" title={portfolio.name}>
            {portfolio.name}
          </span>
        ),
        onClick: () => navigate(`/portfolios/${portfolio.id}`),
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
      label: userName ?? 'Профиль',
    },
    {
      key: 'logout',
      icon: <LogoutOutlined />,
      label: 'Выйти',
      onClick: onLogout,
      danger: true,
    },
  ];

  return (
    <Sider className="app-sidebar" collapsible breakpoint="lg" collapsedWidth="0">
      <div style={{ color: '#fff', padding: '16px', fontSize: 18, fontWeight: 700 }}>
        💹 FinanceApp
      </div>
      <Menu
        className="app-sidebar-menu"
        theme="dark"
        mode="inline"
        inlineIndent={20}
        defaultOpenKeys={resolvedDefaultOpenKeys}
        selectedKeys={selectedKeys}
        items={menuItems}
      />
    </Sider>
  );
};

export default AppSidebar;
