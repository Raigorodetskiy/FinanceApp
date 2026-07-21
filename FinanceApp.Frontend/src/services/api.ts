import axios from 'axios';
import type {
  User,
  Portfolio,
  Stock,
  Order,
  Transaction,
  Dividend,
  PortfolioBalance,
  LoginRequest,
  RegisterRequest,
  CreatePortfolioRequest,
  AddPortfolioItemRequest,
  CreateStockRequest,
  UpdateStockRequest,
  StockHistoryPoint,
  StockHistoryRange,
  CreateOrderRequest,
  UpdateOrderRequest,
  CreateTransactionRequest,
  CreateDividendRequest,
} from '../types';

export const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://173.249.42.11:5000/api';

const api = axios.create({
  baseURL: API_BASE,
});

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('token');
  if (token) {
    config.headers.Authorization = 'Bearer ' + token;
  }
  return config;
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem('token');
      window.location.href = '/login';
    }
    return Promise.reject(error);
  }
);

// Auth
export const login = (data: LoginRequest) =>
  api.post<{ token: string }>('/Auth/login', data);

export const register = (data: RegisterRequest) =>
  api.post('/Users/register', data);

// Users
export const getMe = () => api.get<User>('/Users/me');
export const deleteMe = () => api.delete('/Users/me');

// Portfolios
export const getPortfolios = () => api.get<Portfolio[]>('/Portfolios');
export const getPortfolio = (id: number) => api.get<Portfolio>(`/Portfolios/${id}`);
export const createPortfolio = (data: CreatePortfolioRequest) => api.post<Portfolio>('/Portfolios', data);
export const deletePortfolio = (id: number) => api.delete(`/Portfolios/${id}`);
export const addPortfolioItem = (portfolioId: number, data: AddPortfolioItemRequest) =>
  api.post(`/Portfolios/${portfolioId}/items`, data);
export const updatePortfolioItem = (portfolioId: number, itemId: number, data: AddPortfolioItemRequest) =>
  api.put(`/Portfolios/${portfolioId}/items/${itemId}`, data);
export const deletePortfolioItem = (portfolioId: number, itemId: number) =>
  api.delete(`/Portfolios/${portfolioId}/items/${itemId}`);

// Orders
export const getOrders = (portfolioId: number) =>
  api.get<Order[]>(`/Portfolios/${portfolioId}/orders`);
export const createOrder = (portfolioId: number, data: CreateOrderRequest) =>
  api.post<Order>(`/Portfolios/${portfolioId}/orders`, data);
export const updateOrder = (portfolioId: number, orderId: number, data: UpdateOrderRequest) =>
  api.put<Order>(`/Portfolios/${portfolioId}/orders/${orderId}`, data);
export const deleteOrder = (portfolioId: number, orderId: number) =>
  api.delete(`/Portfolios/${portfolioId}/orders/${orderId}`);

// Stocks
export const getStocks = () => api.get<Stock[]>('/Stocks');
export const getStock = (id: number) => api.get<Stock>(`/Stocks/${id}`);
export const createStock = (data: CreateStockRequest) => api.post<Stock>('/Stocks', data);
export const updateStock = (id: number, data: UpdateStockRequest) => api.put<Stock>(`/Stocks/${id}`, data);
export const deleteStock = (id: number) => api.delete(`/Stocks/${id}`);
export const getStockHistory = (id: number, range: StockHistoryRange) =>
  api.get<StockHistoryPoint[]>(`/Stocks/${id}/history`, { params: { range } });

// Stock prices
export const getStockPrice = (symbol: string) =>
  api.get<{ symbol: string; currentPrice: number; change: number; percentChange: number; marketState: string }>(`/StockPrice/${symbol}`);
export const getEurUsdRate = () =>
  api.get<{ eurUsd: number }>('/StockPrice/rate/eurusd');

// Finance
export const getBalance = (portfolioId: number) =>
  api.get<PortfolioBalance>(`/Portfolios/${portfolioId}/finance/balance`);
export const getTransactions = (portfolioId: number) =>
  api.get<Transaction[]>(`/Portfolios/${portfolioId}/finance/transactions`);
export const createTransaction = (portfolioId: number, data: CreateTransactionRequest) =>
  api.post<Transaction>(`/Portfolios/${portfolioId}/finance/transactions`, data);
export const deleteTransaction = (portfolioId: number, id: number) =>
  api.delete(`/Portfolios/${portfolioId}/finance/transactions/${id}`);
export const getDividends = (portfolioId: number) =>
  api.get<Dividend[]>(`/Portfolios/${portfolioId}/finance/dividends`);
export const createDividend = (portfolioId: number, data: CreateDividendRequest) =>
  api.post<Dividend>(`/Portfolios/${portfolioId}/finance/dividends`, data);
export const deleteDividend = (portfolioId: number, id: number) =>
  api.delete(`/Portfolios/${portfolioId}/finance/dividends/${id}`);

export default api;
