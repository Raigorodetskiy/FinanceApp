export type StockExchange = 'NYSE' | 'NASDAQ' | 'Frankfurt';

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

export interface IndustryRef {
  id: number;
  name: string;
  isArchived: boolean;
}

export interface SectorRef {
  id: number;
  name: string;
  isArchived: boolean;
}

export interface IndustryDto {
  id: number;
  sectorId: number;
  name: string;
  normalizedName: string;
  isArchived: boolean;
  sortOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  stockCount: number;
}

export interface SectorDto {
  id: number;
  name: string;
  normalizedName: string;
  isArchived: boolean;
  sortOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  industryCount: number;
  stockCount: number;
  industries: IndustryDto[];
}

export interface MarketIndex {
  id: number;
  name: string;
  code: string;
  providerSymbol?: string | null;
  description: string;
  countryOrRegion: string;
  sortOrder: number;
  isArchived: boolean;
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
  /** Persisted absolute daily change in the application currency (EUR). Null when unavailable. */
  currentPriceChange?: number | null;
  /** Persisted percentage daily change. Null when unavailable. */
  currentPriceChangePercent?: number | null;
  /** UTC timestamp of the price as reported by the quote provider. Null when unavailable. */
  currentPriceAt?: string | null;
  industryId?: number | null;
  industry?: IndustryRef | null;
  sector?: SectorRef | null;
  marketIndexIds?: number[];
  /** Tracking status. 0 = CatalogOnly, 1 = Tracked. Tracked stocks appear in the main table and participate in price updates. */
  trackingStatus?: StockTrackingStatus;
  /** Symbol as provided by the data provider used to import this stock. */
  providerSymbol?: string | null;
}

export enum StockTrackingStatus {
  CatalogOnly = 0,
  Tracked = 1,
}

export type StockHistoryRange = '5y' | '3y' | '1y' | '6m' | '3m' | '1m' | '1w' | '24h' | 'today';
export type MarketIndexHistoryRange = StockHistoryRange;

export interface MarketIndexHistoryPoint {
  timestamp: string;
  interval: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number | null;
}

export interface MarketIndexHistoryResponse {
  marketIndexId: number;
  range: MarketIndexHistoryRange;
  interval: string;
  isStale: boolean;
  staleReason: string | null;
  points: MarketIndexHistoryPoint[];
}

export interface MarketIndexRefreshResponse {
  marketIndexId: number;
  deletedPoints: number;
  importedPoints: number;
}

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
  /** Current regular-session day high in raw quote units. Null when unavailable. */
  rawDayHigh: number | null;
  /** Current regular-session day low in raw quote units. Null when unavailable. */
  rawDayLow: number | null;
  /** Day high after normalization (e.g. GBp → GBP). Null when unavailable. */
  normalizedDayHigh: number | null;
  /** Day low after normalization. Null when unavailable. */
  normalizedDayLow: number | null;
  /** Day high converted to EUR. Null when unavailable or no rate. */
  dayHighEur: number | null;
  /** Day low converted to EUR. Null when unavailable or no rate. */
  dayLowEur: number | null;
  marketState: string;
  /** Session the returned price belongs to (e.g. "REGULAR", "LAST"). Distinct from marketState. */
  priceSession: string;
  /** UTC timestamp of the price from the provider. Null when not supplied. */
  priceTimestampUtc: string | null;
  /** True when priceTimestampUtc is present and older than 24 hours. */
  isStale: boolean;
  /** Human-readable delay warning from the provider. Non-null when the quote is considered delayed. */
  delayWarning?: string | null;
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
  volumeMetrics: StockHistoryVolumeMetrics;
  points: StockHistoryPoint[];
}

export interface StockHistoryVolumeMetrics {
  averageVolume20: number | null;
  averageVolume50: number | null;
  relativeVolume: number | null;
  turnover: number | null;
  turnoverCurrency: string | null;
  latestMetricsTimestamp: string | null;
  usesCompletedCandle: boolean;
}

export interface StockHistoryRefreshResponse {
  stockId: number;
  deletedPoints: number;
  importedPoints: number;
  rateLimited?: boolean;
}

export type FundamentalsState = 'Fresh' | 'Stale' | 'Unavailable';
export type FinancialPeriodType = 'Annual' | 'Quarterly';
export type EarningsDateStatus = 'Estimated' | 'Confirmed' | 'Unknown';

export interface FundamentalsSnapshot {
  id: number;
  sourceSymbol: string;
  marketCap: number | null;
  enterpriseValue: number | null;
  totalDebt: number | null;
  cashAndEquivalents: number | null;
  revenueTtm: number | null;
  netIncomeTtm: number | null;
  ebitdaTtm: number | null;
  operatingIncomeTtm: number | null;
  freeCashFlowTtm: number | null;
  totalAssets: number | null;
  totalLiabilities: number | null;
  peRatio: number | null;
  forwardPeRatio: number | null;
  pbRatio: number | null;
  dividendYield: number | null;
  currency: string | null;
  source: string;
  asOfDate: string | null;
  fetchedAtUtc: string;
}

export interface FinancialPeriodDto {
  id: number;
  periodType: FinancialPeriodType;
  fiscalYear: number | null;
  fiscalQuarter: number | null;
  periodEndDate: string | null;
  reportedCurrency: string | null;
  revenue: number | null;
  operatingIncome: number | null;
  netIncome: number | null;
  epsReported: number | null;
  epsEstimate: number | null;
  ebitda: number | null;
  totalDebt: number | null;
  totalAssets: number | null;
  totalLiabilities: number | null;
  freeCashFlow: number | null;
  source: string;
  asOfDate: string | null;
  fetchedAtUtc: string;
}

export interface EarningsEventDto {
  id: number;
  reportDate: string | null;
  reportDateEnd: string | null;
  dateStatus: EarningsDateStatus;
  epsEstimate: number | null;
  epsReported: number | null;
  revenueEstimate: number | null;
  revenueReported: number | null;
  fiscalPeriod: string | null;
  source: string;
  fetchedAtUtc: string;
}

export interface FundamentalsResponse {
  stockId: number;
  state: FundamentalsState;
  warningMessage: string | null;
  snapshot: FundamentalsSnapshot | null;
  periods: FinancialPeriodDto[];
  earningsEvents: EarningsEventDto[];
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
export type InstrumentCodeType = 'ISIN' | 'Ticker';

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
  instrumentCode: string | null;
  instrumentCodeType: InstrumentCodeType | null;
  quantity: number | null;
  unitPrice: number | null;
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
  instrumentCode?: string | null;
  instrumentCodeType?: InstrumentCodeType | null;
  quantity?: number | null;
  unitPrice?: number | null;
}

export interface UpdateTransactionRequest {
  type: TransactionType;
  amount: number;
  createdAt: string;
  stockId?: number | null;
  description?: string;
  instrumentCode?: string | null;
  instrumentCodeType?: InstrumentCodeType | null;
  quantity?: number | null;
  unitPrice?: number | null;
}

export interface UpdatePortfolioBalanceRequest {
  cashBalance: number;
  /** @deprecated Broker credit no longer affects totals. Omit from new requests. */
  brokerCredit?: number;
}

export interface LoginRequest {
  identifier?: string;
  email?: string;
  password: string;
}

export interface UpdateProfileRequest {
  username: string;
  currentPassword: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
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
  industryId?: number | null;
  marketIndexIds?: number[];
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
  /** Persisted absolute daily change in normalized/application currency. Null when unavailable. */
  currentPriceChange?: number | null;
  /** Persisted percentage daily change. Null when unavailable. */
  currentPriceChangePercent?: number | null;
  /** UTC timestamp of the price from the quote provider. Null when unavailable. */
  currentPriceAt?: string | null;
}

export interface UpdateStockMetadataRequest {
  name: string;
  commonName?: string;
  wkn?: string | null;
  isin?: string | null;
  finanzenNetSlug?: string | null;
  currentPrice: number;
  industryId?: number | null;
  marketIndexIds?: number[];
}

export interface CreateMarketIndexRequest {
  name: string;
  code: string;
  providerSymbol?: string | null;
  description?: string;
  countryOrRegion?: string;
  sortOrder?: number;
}

export interface UpdateMarketIndexRequest {
  name: string;
  code: string;
  providerSymbol?: string | null;
  description?: string;
  countryOrRegion?: string;
  sortOrder?: number;
}

export interface CreateSectorRequest {
  name: string;
  sortOrder?: number;
}

export interface UpdateSectorRequest {
  name: string;
  sortOrder: number;
}

export interface CreateIndustryRequest {
  name: string;
  sortOrder?: number;
}

export interface UpdateIndustryRequest {
  name: string;
  sortOrder: number;
  sectorId?: number;
}

export interface MoveIndustryRequest {
  targetSectorId: number;
}

export interface UpdateStockQuoteRequest {
  currentPrice: number;
  currentPriceChange: number | null;
  currentPriceChangePercent: number | null;
  currentPriceAt: string | null;
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

export interface UpdateStockQuoteResponse {
  stockId: number;
  currentPrice: number;
  currentPriceChange: number | null;
  currentPriceChangePercent: number | null;
  currentPriceAt: string | null;
  applied: boolean;
}

// ── Index constituents ──────────────────────────────────────────────────────

export interface IndexConstituentDto {
  stockId: number;
  ticker: string;
  providerSymbol?: string | null;
  name: string;
  commonName?: string | null;
  exchange: StockExchange;
  isin?: string | null;
  wkn?: string | null;
  finanzenNetSlug?: string | null;
  currentPrice?: number | null;
  currentPriceChange?: number | null;
  currentPriceChangePercent?: number | null;
  currentPriceAt?: string | null;
  /** "CatalogOnly" or "Tracked" */
  trackingStatus: string;
  source?: string | null;
  providerConstituentKey?: string | null;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  lastVerifiedAt?: string | null;
  importedAt: string;
}

export interface IndexConstituentsResponse {
  marketIndexId: number;
  indexName: string;
  totalCount: number;
  source?: string | null;
  asOfDate?: string | null;
  isCuratedSnapshot?: boolean;
  isStale?: boolean;
  staleReason?: string | null;
  constituents: IndexConstituentDto[];
}

export interface IndexConstituentsRefreshResponse {
  marketIndexId: number;
  providerStatus: string;
  providerName?: string | null;
  providerMessage?: string | null;
  fetchedAt?: string | null;
  asOfDate?: string | null;
  sourceUrl?: string | null;
  isCuratedSnapshot?: boolean;
  isStale?: boolean;
  added: number;
  updated: number;
  unchanged: number;
  closed: number;
  conflicts?: number;
}

export interface IndexConstituentHistoryRefreshItemResponse {
  stockId: number;
  ticker: string;
  exchange: string;
  status: 'Succeeded' | 'Failed' | 'RateLimited' | 'SkippedRateLimited';
  deletedPoints: number;
  importedPoints: number;
  error?: string | null;
}

export type IndexConstituentHistoryRefreshJobState =
  | 'Queued'
  | 'Running'
  | 'Succeeded'
  | 'RateLimited'
  | 'Failed'
  | 'Interrupted';

export interface IndexConstituentHistoryRefreshJobResponse {
  jobId: string;
  marketIndexId: number;
  stockId: number;
  state: IndexConstituentHistoryRefreshJobState;
  reusedActiveJob: boolean;
  statusUrl?: string | null;
  createdAtUtc: string;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  expiresAtUtc?: string | null;
  deletedPoints: number;
  importedPoints: number;
  error?: string | null;
}

export interface IndexConstituentHistoryRefreshBatchResponse {
  marketIndexId: number;
  total: number;
  attempted: number;
  succeeded: number;
  failed: number;
  rateLimited: number;
  skippedRateLimited: number;
  stoppedDueToRateLimit: boolean;
  detailsTruncated: boolean;
  results: IndexConstituentHistoryRefreshItemResponse[];
}

export type IndexConstituentsBatchQuoteRefreshJobState =
  | 'Queued'
  | 'Running'
  | 'Succeeded'
  | 'RateLimited'
  | 'Failed'
  | 'Interrupted';

export interface IndexConstituentsBatchQuoteRefreshJobResponse {
  jobId: string;
  marketIndexId: number;
  state: IndexConstituentsBatchQuoteRefreshJobState;
  reusedActiveJob: boolean;
  statusUrl?: string | null;
  createdAtUtc: string;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  expiresAtUtc?: string | null;
  total: number;
  processed: number;
  remaining: number;
  succeeded: number;
  delayed: number;
  noEurConversion: number;
  staleRejected: number;
  providerFailed: number;
  persistFailed: number;
  rateLimited: number;
  rateLimitRetries: number;
  rateLimitedSkipped: number;
  isWaitingForRetry: boolean;
  nextRetryAtUtc?: string | null;
  error?: string | null;
}
