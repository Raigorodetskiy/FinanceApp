export interface User {
  id: number;
  username: string;
  email: string;
  createdAt: string;
  portfolios: Portfolio[];
}

export interface Portfolio {
  id: number;
  name: string;
  userId: number;
  createdAt: string;
  items: PortfolioItem[];
}

export interface PortfolioItem {
  id: number;
  portfolioId: number;
  stockId: number;
  stock: Stock;
  quantity: number;
  buyPrice: number;
  boughtAt: string;
}

export interface Stock {
  id: number;
  ticker: string;
  name: string;
  exchange: string;
  currentPrice: number;
  updatedAt: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  username: string;
  email: string;
  password: string;
}

export interface CreatePortfolioRequest {
  name: string;
}

export interface AddPortfolioItemRequest {
  stockId: number;
  quantity: number;
  buyPrice: number;
}

export interface CreateStockRequest {
  ticker: string;
  name: string;
  exchange: string;
  currentPrice: number;
}

export interface UpdateStockRequest {
  id: number;
  ticker: string;
  name: string;
  exchange: string;
  currentPrice: number;
  updatedAt: string;
}
