export type StockExchange = 'NYSE' | 'Frankfurt';

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
  commonName: string;
  exchange: StockExchange;
  currentPrice: number;
  updatedAt: string;
  wkn?: string | null;
  isin?: string | null;
  /** Optional finanzen.net instrument slug (e.g. "microsoft-aktie"). Used for experimental pre-market enrichment. */
  finanzenNetSlug?: string | null;
}

export type StockHistoryRange = '5y' | '3y' | '1y' | '6m' | '3m' | '1m' | '1w' | '24h' | 'today';

export interface StockQuoteResponse {
  symbol: string;
  rawCurrentPrice: number;
  rawPreviousClose: number;
  rawChange: number;
  currency: string | null;
  financialCurrency: string | null;
  normalizedQuoteCurrency: string | null;
  quoteUnitMultiplier: number;
  normalizedCurrentPrice: number;
  normalizedPreviousClose: number;
  normalizedChange: number;
  currentPriceEur: number | null;
  changeEur: number | null;
  percentChange: number;
  marketState: string;
  /** Session the returned price belongs to (e.g. "REGULAR", "LAST"). Distinct from marketState. */
  priceSession: string;
  /** UTC timestamp of the price from the provider. Null when not supplied. */
  priceTimestampUtc: string | null;
  /** True when priceTimestampUtc is present and older than 24 hours. */
  isStale: boolean;
  /** Identifies which quote provider supplied rawCurrentPrice. Null for Yahoo/Finnhub (primary providers). */
  priceSource: string | null;
  rateToEur: number | null;
  rateTimestampUtc: string | null;
  rateSource: string | null;
  conversionWarning: string | null;
}

export interface StockHistoryPoint {
  timestamp: string;
  interval: string;
  openRaw: number;
  highRaw: number;
  lowRaw: number;
  closeRaw: number;
  openNormalized: number;
  highNormalized: number;
  lowNormalized: number;
  closeNormalized: number;
  openEur: number | null;
  highEur: number | null;
  lowEur: number | null;
  closeEur: number | null;
  volume: number;
}

export interface StockHistoryResponse {
  range: StockHistoryRange;
  interval: string;
  currency: string | null;
  financialCurrency: string | null;
  normalizedQuoteCurrency: string | null;
  quoteUnitMultiplier: number;
  rateToEur: number | null;
  rateTimestampUtc: string | null;
  rateSource: string | null;
  conversionWarning: string | null;
  points: StockHistoryPoint[];
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

export type TransactionType = 'Deposit' | 'Withdrawal' | 'Buy' | 'Sell' | 'Dividend';

export interface Transaction {
  id: number;
  portfolioId: number;
  type: TransactionType;
  amount: number;
  signedAmount: number;
  description: string | null;
  createdAt: string;
  stockId: number | null;
  stock: Stock | null;
  orderId: number | null;
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
  createdAt: string;
  stockId?: number | null;
  description?: string;
}

export interface UpdateTransactionRequest {
  type: TransactionType;
  amount: number;
  createdAt: string;
  stockId?: number | null;
  description?: string;
}

export interface UpdatePortfolioBalanceRequest {
  cashBalance: number;
  brokerCredit: number;
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
  commonName?: string;
  exchange: StockExchange;
  currentPrice: number;
  wkn?: string | null;
  isin?: string | null;
  finanzenNetSlug?: string | null;
}

export interface UpdateStockRequest {
  id: number;
  ticker: string;
  name: string;
  commonName?: string;
  exchange: StockExchange;
  currentPrice: number;
  updatedAt: string;
  wkn?: string | null;
  isin?: string | null;
  finanzenNetSlug?: string | null;
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
