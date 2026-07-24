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
  brokerCredit: number;
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

export type StockHistoryRange = '5y' | '3y' | '1y' | '6m' | '3m' | '1m' | '1w' | '24h' | 'today';

export interface StockHistoryPoint {
  timestamp: string;
  interval: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
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

export type TransactionType = 'Deposit' | 'Withdrawal';

export interface Transaction {
  id: number;
  portfolioId: number;
  type: TransactionType;
  amount: number;
  signedAmount: number;
  description: string | null;
  createdAt: string;
}

export interface Dividend {
  id: number;
  portfolioId: number;
  stockId: number;
  stock: Stock;
  amount: number;
  paidAt: string;
  createdAt: string;
}

export interface PortfolioBalance {
  cashBalance: number;
  brokerCredit: number;
  totalBalance: number;
  stocksValue: number;
  totalPortfolioValue: number;
}

export interface CreateTransactionRequest {
  type: TransactionType;
  amount: number;
  signedAmount?: number;
  description?: string;
}

export interface CreateDividendRequest {
  stockId: number;
  amount: number;
  paidAt: string;
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
