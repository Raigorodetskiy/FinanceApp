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
  orders: Order[];
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

export type OrderType = 'Buy' | 'Sell';
export type OrderStatus = 'Pending' | 'Executed' | 'Cancelled';

export interface Order {
  id: number;
  portfolioId: number;
  stockId: number;
  stock: Stock;
  type: OrderType;
  status: OrderStatus;
  quantity: number;
  price: number;
  stopLoss: number | null;
  stopMarket: number | null;
  createdAt: string;
  executedAt: string | null;
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

export interface CreateOrderRequest {
  stockId: number;
  type: OrderType;
  quantity: number;
  price: number;
  stopLoss?: number;
  stopMarket?: number;
}

export interface UpdateOrderRequest {
  type: OrderType;
  status: OrderStatus;
  quantity: number;
  price: number;
  stopLoss?: number;
  stopMarket?: number;
}
