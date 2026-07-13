import client from './client';
import { Stock } from '../types';

export const getStocks = () =>
  client.get<Stock[]>('/stocks');

export const getStockById = (id: number) =>
  client.get<Stock>(`/stocks/${id}`);
