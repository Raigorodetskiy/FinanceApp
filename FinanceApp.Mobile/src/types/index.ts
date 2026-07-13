export interface User {
  id: number;
  username: string;
  email: string;
}

export interface Stock {
  id: number;
  ticker: string;
  name: string;
  exchange: string;
  currentPrice: number;
  updatedAt: string;
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

export interface Portfolio {
  id: number;
  name: string;
  userId: number;
  items: PortfolioItem[];
}

export interface AuthResponse {
  token: string;
  expires: string;
  user: User;
}

export interface LoginDto {
  email: string;
  password: string;
}
