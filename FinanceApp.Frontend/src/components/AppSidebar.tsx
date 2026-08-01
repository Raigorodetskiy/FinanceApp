import React from 'react';
import { DashboardOutlined, FolderOutlined, LogoutOutlined, StockOutlined, UnorderedListOutlined, UserOutlined, WalletOutlined } from '@ant-design/icons';
import { Layout, Menu } from 'antd';
import type { MenuProps } from 'antd';
import { useNavigate } from 'react-router-dom';
import type { Portfolio } from '../types';
import './AppSidebar.css';

const { Sider } = Layout;
const PORTFOLIO_KEY_PREFIX = 'portfolio-';

export type PortfolioSection = 'positions' | 'transactions';

interface AppSidebarProps {
  portfolios: Portfolio[];
  selectedKeys: string[];
  userName?: string;
  onLogout: () => void;
  defaultOpenKeys?: string[];
  /** ID of the currently viewed portfolio, if any */
  activePortfolioId?: string | number;
}

const resolveDefaultOpenKeys = (
  selectedKeys: string[],
  activePortfolioId?: string | number,
  defaultOpenKeys?: string[],
): string[] => {
  if (defaultOpenKeys) return defaultOpenKeys;
  const base: string[] = selectedKeys.some((k) => k.startsWith(PORTFOLIO_KEY_PREFIX))
    ? ['portfolios']
    : [];
  if (activePortfolioId != null) {
    base.push(`${PORTFOLIO_KEY_PREFIX}${activePortfolioId}`);
  }
  return base;
};

const AppSidebar: React.FC<AppSidebarProps> = ({
  portfolios,
  selectedKeys,
  userName,
  onLogout,
  defaultOpenKeys,
  activePortfolioId,
}) => {
  const navigate = useNavigate();
  const hasPortfolios = portfolios.length > 0;
  const portfolioRootClassName = hasPortfolios
    ? 'portfolio-tree-root portfolio-tree-root--populated'
    : 'portfolio-tree-root';
  const resolvedDefaultOpenKeys = resolveDefaultOpenKeys(selectedKeys, activePortfolioId, defaultOpenKeys);

  const buildPortfolioChildren = (portfolio: Portfolio): NonNullable<MenuProps['items']> => {
    const pid = portfolio.id;
    return [
      {
        key: `${PORTFOLIO_KEY_PREFIX}${pid}-positions`,
        className: 'portfolio-section-node',
        icon: <UnorderedListOutlined />,
        label: 'Позиции',
        onClick: () => navigate(`/portfolios/${pid}?section=positions`),
      },
      {
        key: `${PORTFOLIO_KEY_PREFIX}${pid}-transactions`,
        className: 'portfolio-section-node',
        icon: <WalletOutlined />,
        label: 'Транзакции',
        onClick: () => navigate(`/portfolios/${pid}?section=transactions`),
      },
    ];
  };

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
      children: portfolios.map((portfolio) => {
        const isActive = activePortfolioId != null && String(portfolio.id) === String(activePortfolioId);
        if (isActive) {
          return {
            key: `${PORTFOLIO_KEY_PREFIX}${portfolio.id}`,
            className: 'portfolio-tree-node portfolio-tree-node--expanded',
            label: (
              <span className="portfolio-tree-node-label" title={portfolio.name}>
                {portfolio.name}
              </span>
            ),
            children: buildPortfolioChildren(portfolio),
          };
        }
        return {
          key: `${PORTFOLIO_KEY_PREFIX}${portfolio.id}`,
          className: 'portfolio-tree-node',
          label: (
            <span className="portfolio-tree-node-label" title={portfolio.name}>
              {portfolio.name}
            </span>
          ),
          onClick: () => navigate(`/portfolios/${portfolio.id}`),
        };
      }),
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
        inlineIndent={16}
        defaultOpenKeys={resolvedDefaultOpenKeys}
        selectedKeys={selectedKeys}
        items={menuItems}
      />
    </Sider>
  );
};

export default AppSidebar;
