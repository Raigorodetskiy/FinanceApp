import React, { useState, useMemo } from 'react';
import {
  DashboardOutlined,
  FolderOutlined,
  FolderOpenOutlined,
  LogoutOutlined,
  StockOutlined,
  UnorderedListOutlined,
  UserOutlined,
  WalletOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  SearchOutlined,
} from '@ant-design/icons';
import { Button, Drawer, Input, Layout, Menu, Tooltip } from 'antd';
import type { MenuProps } from 'antd';
import { useNavigate } from 'react-router-dom';
import type { Portfolio } from '../types';
import './AppSidebar.css';

const { Sider } = Layout;
const PORTFOLIO_KEY_PREFIX = 'portfolio-';
const SIDEBAR_EXPANDED_WIDTH = 260;

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

const labelMatches = (label: string, query: string): boolean =>
  !query || label.toLowerCase().includes(query.toLowerCase());

const AppSidebar: React.FC<AppSidebarProps> = ({
  portfolios,
  selectedKeys,
  userName,
  onLogout,
  defaultOpenKeys,
  activePortfolioId,
}) => {
  const navigate = useNavigate();
  const [collapsed, setCollapsed] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [search, setSearch] = useState('');

  const resolvedDefaultOpenKeys = resolveDefaultOpenKeys(selectedKeys, activePortfolioId, defaultOpenKeys);

  const buildPortfolioChildren = (portfolio: Portfolio): NonNullable<MenuProps['items']> => {
    const pid = portfolio.id;
    const children = [
      {
        key: `${PORTFOLIO_KEY_PREFIX}${pid}-positions`,
        className: 'sidebar-leaf-item',
        icon: <UnorderedListOutlined />,
        label: 'Позиции',
        onClick: () => { navigate(`/portfolios/${pid}?section=positions`); setMobileOpen(false); },
      },
      {
        key: `${PORTFOLIO_KEY_PREFIX}${pid}-transactions`,
        className: 'sidebar-leaf-item',
        icon: <WalletOutlined />,
        label: 'Транзакции',
        onClick: () => { navigate(`/portfolios/${pid}?section=transactions`); setMobileOpen(false); },
      },
    ];
    if (!search) return children;
    return children.filter((c) => labelMatches(c.label as string, search));
  };

  const allMenuItems: MenuProps['items'] = useMemo(() => {
    const q = search.trim();

    const portfolioChildren: NonNullable<MenuProps['items']> = portfolios
      .filter((p) => !q || labelMatches(p.name, q) || labelMatches('Позиции', q) || labelMatches('Транзакции', q))
      .map((portfolio) => {
        const pid = portfolio.id;
        const isActive = activePortfolioId != null && String(portfolio.id) === String(activePortfolioId);
        const children = buildPortfolioChildren(portfolio);

        if (isActive) {
          return {
            key: `${PORTFOLIO_KEY_PREFIX}${pid}`,
            className: 'sidebar-portfolio-node sidebar-portfolio-node--active',
            icon: <FolderOpenOutlined />,
            label: (
              <Tooltip title={portfolio.name} placement="right">
                <span className="sidebar-node-label">{portfolio.name}</span>
              </Tooltip>
            ),
            children,
          };
        }
        return {
          key: `${PORTFOLIO_KEY_PREFIX}${pid}`,
          className: 'sidebar-portfolio-node',
          icon: <FolderOutlined />,
          label: (
            <Tooltip title={portfolio.name} placement="right">
              <span className="sidebar-node-label">{portfolio.name}</span>
            </Tooltip>
          ),
          onClick: () => { navigate(`/portfolios/${pid}`); setMobileOpen(false); },
          children: undefined,
        };
      });

    const items: NonNullable<MenuProps['items']> = [];

    if (labelMatches('Главная', q)) {
      items.push({
        key: 'dashboard',
        icon: <DashboardOutlined />,
        label: 'Главная',
        onClick: () => { navigate('/'); setMobileOpen(false); },
      });
    }

    if (!q || portfolioChildren.length > 0 || labelMatches('Портфели', q)) {
      items.push({
        key: 'portfolios',
        icon: <FolderOutlined />,
        label: 'Портфели',
        children: portfolioChildren,
      });
    }

    if (labelMatches('Акции', q)) {
      items.push({
        key: 'stocks',
        icon: <StockOutlined />,
        label: 'Акции',
        onClick: () => { navigate('/stocks'); setMobileOpen(false); },
      });
    }

    return items;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [portfolios, activePortfolioId, search]);

  const bottomItems: NonNullable<MenuProps['items']> = [
    {
      key: 'profile',
      icon: <UserOutlined />,
      label: (
        <Tooltip title={userName ?? 'Профиль'} placement="right">
          <span className="sidebar-node-label">{userName ?? 'Профиль'}</span>
        </Tooltip>
      ),
    },
    {
      key: 'logout',
      icon: <LogoutOutlined />,
      label: 'Выйти',
      onClick: onLogout,
      className: 'sidebar-logout-item',
      danger: true,
    },
  ];

  const sidebarContent = (
    <div className="sidebar-inner">
      {/* Brand header */}
      <div className="sidebar-brand">
        <span className="sidebar-brand-icon">💹</span>
        {!collapsed && <span className="sidebar-brand-text">FinanceApp</span>}
        <Button
          type="text"
          size="small"
          className="sidebar-collapse-btn"
          icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
          onClick={() => setCollapsed((c) => !c)}
          aria-label={collapsed ? 'Развернуть панель' : 'Свернуть панель'}
        />
      </div>

      {/* Search */}
      {!collapsed && (
        <div className="sidebar-search-wrap">
          <Input
            size="small"
            prefix={<SearchOutlined className="sidebar-search-icon" />}
            placeholder="Поиск…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            allowClear
            className="sidebar-search"
            aria-label="Поиск по навигации"
          />
        </div>
      )}

      {/* Main nav */}
      <div className="sidebar-nav-scroll">
        <Menu
          className="sidebar-menu"
          theme="dark"
          mode="inline"
          inlineIndent={14}
          defaultOpenKeys={resolvedDefaultOpenKeys}
          selectedKeys={selectedKeys}
          items={allMenuItems}
          inlineCollapsed={collapsed}
        />
      </div>

      {/* Bottom account area */}
      <div className="sidebar-bottom">
        <Menu
          className="sidebar-menu sidebar-menu--bottom"
          theme="dark"
          mode="inline"
          inlineIndent={14}
          selectedKeys={[]}
          items={bottomItems}
          inlineCollapsed={collapsed}
        />
      </div>
    </div>
  );

  return (
    <>
      {/* Desktop sidebar via Ant Design Sider for proper Layout integration */}
      <Sider
        className="app-sidebar"
        width={SIDEBAR_EXPANDED_WIDTH}
        collapsedWidth={56}
        collapsed={collapsed}
        collapsible={false}
        breakpoint="md"
        onBreakpoint={(broken) => {
          if (broken) setCollapsed(true);
        }}
      >
        {sidebarContent}
      </Sider>

      {/* Mobile hamburger – only visible on small screens */}
      <Button
        type="text"
        className="sidebar-mobile-toggle"
        icon={<MenuUnfoldOutlined />}
        onClick={() => setMobileOpen(true)}
        aria-label="Открыть навигацию"
      />

      {/* Mobile drawer */}
      <Drawer
        open={mobileOpen}
        onClose={() => setMobileOpen(false)}
        placement="left"
        width={SIDEBAR_EXPANDED_WIDTH}
        styles={{ body: { padding: 0, background: '#1a2c4e' }, header: { display: 'none' } }}
        aria-label="Мобильная навигация"
      >
        {sidebarContent}
      </Drawer>
    </>
  );
};

export default AppSidebar;
