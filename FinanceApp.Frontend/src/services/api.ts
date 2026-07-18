import axios from 'axios';
import type {
  User,
  Portfolio,
  Stock,
  LoginRequest,
  RegisterRequest,
  CreatePortfolioRequest,
  AddPortfolioItemRequest,
  CreateStockRequest,
  UpdateStockRequest,
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

export const getPortfolio = (id: number) =>
  api.get<Portfolio>(`/Portfolios/${id}`);

export const createPortfolio = (data: CreatePortfolioRequest) =>
  api.post<Portfolio>('/Portfolios', data);

export const deletePortfolio = (id: number) =>
  api.delete(`/Portfolios/${id}`);

export const addPortfolioItem = (id: number, data: AddPortfolioItemRequest) =>
  api.post(`/Portfolios/${id}/items`, data);

// Stocks
export const getStocks = () => api.get<Stock[]>('/Stocks');

export const getStock = (id: number) => api.get<Stock>(`/Stocks/${id}`);

export const createStock = (data: CreateStockRequest) =>
  api.post<Stock>('/Stocks', data);

export const updateStock = (id: number, data: UpdateStockRequest) =>
  api.put<Stock>(`/Stocks/${id}`, data);

export const deleteStock = (id: number) => api.delete(`/Stocks/${id}`);

// Stock prices (Finnhub)
export const getStockPrice = (symbol: string) =>
  api.get<{ symbol: string; currentPrice: number; change: number; percentChange: number }>(`/StockPrice/${symbol}`);

export const getEurUsdRate = () =>
  api.get<{ eurUsd: number }>('/StockPrice/eurusd');

export default api;
