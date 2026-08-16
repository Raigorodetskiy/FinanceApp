import React, { useState, useMemo, useCallback } from 'react';
import {
  ApartmentOutlined,
  BookOutlined,
  CalculatorOutlined,
  DashboardOutlined,
  FolderOutlined,
  FolderOpenOutlined,
  GlobalOutlined,
  LogoutOutlined,
  StockOutlined,
  UnorderedListOutlined,
  UserOutlined,
  WalletOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
} from '@ant-design/icons';
import { Button, Drawer, Grid, Layout, Menu, Tooltip } from 'antd';
import type { MenuProps } from 'antd';
import { useNavigate } from 'react-router-dom';
import type { Portfolio } from '../types';
import './AppSidebar.css';

const { Sider } = Layout;
const { useBreakpoint } = Grid;
const PORTFOLIO_KEY_PREFIX = 'portfolio-';
export const PROFILE_ROUTE = '/profile';
const SIDEBAR_EXPANDED_WIDTH = 270;
const SIDEBAR_COLLAPSED_WIDTH = 64;
const SIDEBAR_COLLAPSED_STORAGE_KEY = 'financeapp.sidebar.collapsed';
const PORTFOLIOS_OPEN_STORAGE_KEY = 'financeapp.sidebar.portfolios.open';
const STOCKS_OPEN_STORAGE_KEY = 'financeapp.sidebar.stocks.open';
const STOCKS_DIRECTORIES_OPEN_STORAGE_KEY = 'financeapp.sidebar.stocks-directories.open';

export type PortfolioSection = 'positions' | 'transactions';

export interface AppSidebarProps {
  portfolios: Portfolio[];
  selectedKeys: string[];
  userName?: string;
  onLogout: () => void;
  defaultOpenKeys?: string[];
  /** ID of the currently viewed portfolio, if any */
  activePortfolioId?: string | number;
}

export interface StocksDirectoriesMenuEntry {
  key: 'sectors' | 'market-indices' | 'financial-metrics';
  label: string;
  route: string;
  icon: React.ReactElement;
}

export const STOCKS_DIRECTORIES_PARENT_KEY = 'stocks-directories';
export const STOCKS_DIRECTORIES_PARENT_LABEL = 'Справочники';
export const STOCKS_DIRECTORIES_PARENT_ICON = <BookOutlined />;

export const STOCKS_DIRECTORIES_MENU_ENTRIES: StocksDirectoriesMenuEntry[] = [
  {
    key: 'sectors',
    label: 'Секторы и отрасли',
    route: '/sectors',
    icon: <ApartmentOutlined />,
  },
  {
    key: 'market-indices',
    label: 'Мировые индексы',
    route: '/market-indices',
    icon: <GlobalOutlined />,
  },
  {
    key: 'financial-metrics',
    label: 'Финансовые показатели',
    route: '/financial-metrics',
    icon: <CalculatorOutlined />,
  },
];

export function buildStocksDirectoriesMenuItems(
  onNavigate: (route: string) => void,
): NonNullable<MenuProps['items']> {
  return STOCKS_DIRECTORIES_MENU_ENTRIES.map((entry) => ({
    key: entry.key,
    icon: entry.icon,
    label: entry.label,
    onClick: () => onNavigate(entry.route),
  }));
}

const AppSidebar: React.FC<AppSidebarProps> = ({
  portfolios,
  selectedKeys,
  userName,
  onLogout,
  defaultOpenKeys,
  activePortfolioId,
}) => {
  const navigate = useNavigate();
  const screens = useBreakpoint();
  const isMobile = !screens.md;
  const [collapsed, setCollapsed] = useState<boolean>(() => {
    if (typeof window === 'undefined') return false;
    try {
      return window.localStorage.getItem(SIDEBAR_COLLAPSED_STORAGE_KEY) === '1';
    } catch {
      return false;
    }
  });
  const [mobileOpen, setMobileOpen] = useState(false);
  const isSidebarCollapsed = !isMobile && collapsed;

  // Controlled portfolios open state: persisted in localStorage.
  // On first load, default to open when on a portfolio route or when explicitly set.
  const [portfoliosOpen, setPortfoliosOpen] = useState<boolean>(() => {
    if (typeof window === 'undefined') {
      return selectedKeys.some((k) => k.startsWith(PORTFOLIO_KEY_PREFIX));
    }
    try {
      const stored = window.localStorage.getItem(PORTFOLIOS_OPEN_STORAGE_KEY);
      if (stored !== null) return stored === '1';
    } catch {}
    // Fall back to route-based detection for first-ever visit
    return selectedKeys.some((k) => k.startsWith(PORTFOLIO_KEY_PREFIX));
  });

  const [stocksOpen, setStocksOpen] = useState<boolean>(() => {
    if (typeof window === 'undefined') {
      return selectedKeys.some((k) => k === 'stocks' || k === 'stocks-list' || k === 'sectors' || k === 'market-indices' || k === 'financial-metrics' || k.startsWith('stocks-'));
    }
    try {
      const stored = window.localStorage.getItem(STOCKS_OPEN_STORAGE_KEY);
      if (stored !== null) return stored === '1';
    } catch {}
    return selectedKeys.some((k) => k === 'stocks' || k === 'stocks-list' || k === 'sectors' || k === 'market-indices' || k === 'financial-metrics' || k.startsWith('stocks-'));
  });

  const [stocksDirectoriesOpen, setStocksDirectoriesOpen] = useState<boolean>(() => {
    if (typeof window === 'undefined') {
      return selectedKeys.some((k) => k === 'sectors' || k === 'market-indices' || k === 'financial-metrics' || k.startsWith('sectors-') || k.startsWith('market-indices-'));
    }
    try {
      const stored = window.localStorage.getItem(STOCKS_DIRECTORIES_OPEN_STORAGE_KEY);
      if (stored !== null) return stored === '1';
    } catch {}
    return selectedKeys.some((k) => k === 'sectors' || k === 'market-indices' || k === 'financial-metrics' || k.startsWith('sectors-') || k.startsWith('market-indices-'));
  });

  React.useEffect(() => {
    if (typeof window === 'undefined') return;
    try {
      window.localStorage.setItem(SIDEBAR_COLLAPSED_STORAGE_KEY, collapsed ? '1' : '0');
    } catch {}
  }, [collapsed]);

  React.useEffect(() => {
    if (typeof window === 'undefined') return;
    try {
      window.localStorage.setItem(PORTFOLIOS_OPEN_STORAGE_KEY, portfoliosOpen ? '1' : '0');
    } catch {}
  }, [portfoliosOpen]);

  React.useEffect(() => {
    if (typeof window === 'undefined') return;
    try {
      window.localStorage.setItem(STOCKS_OPEN_STORAGE_KEY, stocksOpen ? '1' : '0');
    } catch {}
  }, [stocksOpen]);

  React.useEffect(() => {
    if (typeof window === 'undefined') return;
    try {
      window.localStorage.setItem(STOCKS_DIRECTORIES_OPEN_STORAGE_KEY, stocksDirectoriesOpen ? '1' : '0');
    } catch {}
  }, [stocksDirectoriesOpen]);

  // Compute controlled open keys: combine user-controlled state with
  // route-required keys (active portfolio hierarchy must always be visible).
  const openKeys = useMemo((): string[] => {
    const keys: string[] = [];
    const hasStocksSelection = selectedKeys.some((key) =>
      key === 'stocks'
      || key === 'stocks-list'
      || key === 'sectors'
      || key === 'market-indices'
      || key === 'financial-metrics'
      || key.startsWith('stocks-')
    );
    const hasStocksDirectoriesSelection = selectedKeys.some((key) =>
      key === 'stocks-directories'
      || key === 'sectors'
      || key === 'market-indices'
      || key === 'financial-metrics'
      || key.startsWith('sectors-')
      || key.startsWith('market-indices-')
    );
    // Always open portfolios when on a portfolio route, or when user has it open.
    if (portfoliosOpen || activePortfolioId != null) {
      keys.push('portfolios');
    }
    // Open stocks when route requires it OR user has it open.
    if (stocksOpen || hasStocksSelection) {
      keys.push('stocks');
    }
    // Open stocks-directories when route requires it OR user has it open (only when stocks is open).
    if ((stocksOpen || hasStocksSelection) && (stocksDirectoriesOpen || hasStocksDirectoriesSelection)) {
      keys.push('stocks-directories');
    }
    // Keep the specific active portfolio node open.
    if (activePortfolioId != null) {
      keys.push(`${PORTFOLIO_KEY_PREFIX}${activePortfolioId}`);
    }
    // Honour any externally required keys (e.g. from defaultOpenKeys prop).
    if (defaultOpenKeys) {
      for (const k of defaultOpenKeys) {
        if (!keys.includes(k)) keys.push(k);
      }
    }
    return keys;
  }, [portfoliosOpen, stocksOpen, stocksDirectoriesOpen, activePortfolioId, defaultOpenKeys, selectedKeys]);

  // Handle submenu open/close changes from Ant Design.
  const handleMenuOpenChange = useCallback((newOpenKeys: string[]) => {
    const prevHasPortfolios = openKeys.includes('portfolios');
    const nextHasPortfolios = newOpenKeys.includes('portfolios');
    if (prevHasPortfolios && !nextHasPortfolios) {
      if (activePortfolioId == null) {
        setPortfoliosOpen(false);
      }
    } else if (!prevHasPortfolios && nextHasPortfolios) {
      setPortfoliosOpen(true);
    }

    const prevHasStocks = openKeys.includes('stocks');
    const nextHasStocks = newOpenKeys.includes('stocks');
    const routeRequiresStocks = selectedKeys.some((key) =>
      key === 'stocks'
      || key === 'stocks-list'
      || key === 'sectors'
      || key === 'market-indices'
      || key === 'financial-metrics'
      || key.startsWith('stocks-')
    );
    if (prevHasStocks && !nextHasStocks) {
      if (!routeRequiresStocks) {
        setStocksOpen(false);
        setStocksDirectoriesOpen(false);
      }
    } else if (!prevHasStocks && nextHasStocks) {
      setStocksOpen(true);
    }

    const prevHasStocksDirectories = openKeys.includes('stocks-directories');
    const nextHasStocksDirectories = newOpenKeys.includes('stocks-directories');
    const routeRequiresDirectories = selectedKeys.some((key) =>
      key === 'sectors'
      || key === 'market-indices'
      || key === 'financial-metrics'
      || key.startsWith('sectors-')
      || key.startsWith('market-indices-')
    );
    if (prevHasStocksDirectories && !nextHasStocksDirectories) {
      if (!routeRequiresDirectories) {
        setStocksDirectoriesOpen(false);
      }
    } else if (!prevHasStocksDirectories && nextHasStocksDirectories) {
      setStocksDirectoriesOpen(true);
    }
  }, [openKeys, activePortfolioId, selectedKeys]);

  const buildPortfolioChildren = (portfolio: Portfolio): NonNullable<MenuProps['items']> => {
    const pid = portfolio.id;
    return [
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
  };

  const allMenuItems: MenuProps['items'] = useMemo(() => {
    const portfolioChildren: NonNullable<MenuProps['items']> = portfolios
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

    return [
      {
        key: 'dashboard',
        icon: <DashboardOutlined />,
        label: 'Главная',
        onClick: () => { navigate('/'); setMobileOpen(false); },
      },
      {
        key: 'portfolios',
        icon: <FolderOutlined />,
        label: 'Портфели',
        children: portfolioChildren,
      },
      {
        key: 'stocks',
        icon: <StockOutlined />,
        label: 'Акции',
        children: [
          {
            key: 'stocks-list',
            icon: <UnorderedListOutlined />,
            label: 'Список акций',
            onClick: () => { navigate('/stocks'); setMobileOpen(false); },
          },
          {
            key: STOCKS_DIRECTORIES_PARENT_KEY,
            icon: STOCKS_DIRECTORIES_PARENT_ICON,
            label: STOCKS_DIRECTORIES_PARENT_LABEL,
            children: buildStocksDirectoriesMenuItems((route) => {
              navigate(route);
              setMobileOpen(false);
            }),
          },
        ],
      },
    ];
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [portfolios, activePortfolioId]);

  const bottomItems: NonNullable<MenuProps['items']> = [
    {
      key: 'profile',
      icon: <UserOutlined />,
      label: (
        <Tooltip title={userName ?? 'Профиль'} placement="right">
          <span className="sidebar-node-label">{userName ?? 'Профиль'}</span>
        </Tooltip>
      ),
      onClick: () => { navigate(PROFILE_ROUTE); setMobileOpen(false); },
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
        {!isSidebarCollapsed && <span className="sidebar-brand-text">FinanceApp</span>}
      </div>

      {/* Main nav */}
      <div className="sidebar-nav-scroll">
        <Menu
          className="sidebar-menu"
          theme="dark"
          mode="inline"
          inlineIndent={14}
          openKeys={isSidebarCollapsed ? [] : openKeys}
          onOpenChange={handleMenuOpenChange}
          selectedKeys={selectedKeys}
          items={allMenuItems}
          inlineCollapsed={isSidebarCollapsed}
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
          inlineCollapsed={isSidebarCollapsed}
        />
      </div>
    </div>
  );

  return (
    <>
      {/* Desktop: Sider + external collapse button, wrapped together on the gray shell background */}
      <div
        className={`sidebar-desktop-wrap${isSidebarCollapsed ? ' sidebar-desktop-wrap--collapsed' : ''}`}
        aria-hidden={isMobile}
      >
        <Sider
          className="app-sidebar"
          width={SIDEBAR_EXPANDED_WIDTH}
          collapsedWidth={SIDEBAR_COLLAPSED_WIDTH}
          collapsed={isSidebarCollapsed}
          collapsible={false}
          breakpoint="md"
          onBreakpoint={(broken) => {
            if (broken) setMobileOpen(false);
          }}
        >
          {sidebarContent}
        </Sider>
        {/* External collapse/expand button – below the blue panel, on the gray background */}
        {!isMobile && (
          <Button
            type="default"
            className="sidebar-collapse-btn"
            icon={isSidebarCollapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
            onClick={() => setCollapsed((c) => !c)}
            aria-label={isSidebarCollapsed ? 'Развернуть панель' : 'Свернуть панель'}
          />
        )}
      </div>

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
        rootClassName="app-sidebar-drawer"
        open={mobileOpen}
        onClose={() => setMobileOpen(false)}
        placement="left"
        width={SIDEBAR_EXPANDED_WIDTH}
        title="FinanceApp"
        styles={{
          body: { padding: 0, background: '#073f86' },
          header: { background: '#073f86', borderBottom: '1px solid rgba(255, 255, 255, 0.16)' },
          content: { background: '#073f86' },
        }}
        aria-label="Мобильная навигация"
      >
        {sidebarContent}
      </Drawer>
    </>
  );
};

export default AppSidebar;
