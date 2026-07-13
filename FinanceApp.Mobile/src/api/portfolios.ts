import client from './client';
import { Portfolio } from '../types';

export const getPortfoliosByUser = (userId: number) =>
  client.get<Portfolio[]>(`/portfolios/user/${userId}`);

export const createPortfolio = (name: string, userId: number) =>
  client.post<Portfolio>('/portfolios', { name, userId });

export const deletePortfolio = (id: number) =>
  client.delete(`/portfolios/${id}`);

export const addPortfolioItem = (portfolioId: number, stockId: number, quantity: number, buyPrice: number) =>
  client.post(`/portfolios/${portfolioId}/items`, { stockId, quantity, buyPrice });
