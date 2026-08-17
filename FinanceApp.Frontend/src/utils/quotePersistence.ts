import type {
  UpdateStockQuoteRequest,
  UpdateStockQuoteResponse,
  StockQuoteResponse,
} from '../types';

export const buildQuotePatch = (
  quote: StockQuoteResponse,
): UpdateStockQuoteRequest | null => {
  if (quote.currentPriceEur == null) return null;
  const tsRaw = quote.priceTimestampUtc ? Date.parse(quote.priceTimestampUtc) : NaN;
  return {
    currentPrice: Math.round(quote.currentPriceEur * 100) / 100,
    currentPriceChange:
      quote.changeEur != null ? Math.round(quote.changeEur * 10000) / 10000 : null,
    currentPriceChangePercent:
      quote.percentChange != null ? Math.round(quote.percentChange * 10000) / 10000 : null,
    currentPriceAt: isFinite(tsRaw) ? quote.priceTimestampUtc : null,
  };
};

type QuoteSnapshotTarget = {
  currentPrice?: number | null;
  currentPriceChange?: number | null;
  currentPriceChangePercent?: number | null;
  currentPriceAt?: string | null;
};

export const applyPersistedQuoteSnapshot = <T extends QuoteSnapshotTarget>(
  target: T,
  snapshot: Pick<
    UpdateStockQuoteResponse,
    'currentPrice' | 'currentPriceChange' | 'currentPriceChangePercent' | 'currentPriceAt'
  >,
): T => ({
  ...target,
  currentPrice: snapshot.currentPrice,
  currentPriceChange: snapshot.currentPriceChange,
  currentPriceChangePercent: snapshot.currentPriceChangePercent,
  currentPriceAt: snapshot.currentPriceAt,
});
