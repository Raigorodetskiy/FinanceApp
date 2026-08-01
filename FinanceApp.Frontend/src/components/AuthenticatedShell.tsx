import React from 'react';
import { Layout } from 'antd';
import AppSidebar from './AppSidebar';
import type { AppSidebarProps } from './AppSidebar';

const { Header, Content } = Layout;

interface AuthenticatedShellProps extends AppSidebarProps {
  headerLeft: React.ReactNode;
  headerRight?: React.ReactNode;
  children: React.ReactNode;
}

const AuthenticatedShell: React.FC<AuthenticatedShellProps> = ({
  portfolios,
  selectedKeys,
  userName,
  onLogout,
  defaultOpenKeys,
  activePortfolioId,
  headerLeft,
  headerRight,
  children,
}) => (
  <Layout className="app-shell">
    <AppSidebar
      portfolios={portfolios}
      selectedKeys={selectedKeys}
      userName={userName}
      onLogout={onLogout}
      defaultOpenKeys={defaultOpenKeys}
      activePortfolioId={activePortfolioId}
    />
    <Layout className="app-workspace">
      <Header className="app-workspace-header">
        <div className="app-workspace-header-left">{headerLeft}</div>
        {headerRight && <div className="app-workspace-header-right">{headerRight}</div>}
      </Header>
      <Content className="app-workspace-content">{children}</Content>
    </Layout>
  </Layout>
);

export default AuthenticatedShell;
