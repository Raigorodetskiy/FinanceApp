import { describe, expect, it } from 'vitest';
import { STOCK_MARKET_INDEX_SELECT_MODE, buildCreateStockPayload, buildUpdateStockMetadataPayload } from './StocksPage';

describe('StocksPage market indices form support', () => {
  it('uses multiple select mode for market indices', () => {
    expect(STOCK_MARKET_INDEX_SELECT_MODE).toBe('multiple');
  });

  it('includes selected market index ids in create payload', () => {
    const payload = buildCreateStockPayload({
      ticker: 'AAPL',
      name: ' Apple Inc. ',
      commonName: 'Apple',
      exchange: 'NYSE',
      currentPrice: 100,
      marketIndexIds: [1, 3],
    });

    expect(payload.marketIndexIds).toEqual([1, 3]);
  });

  it('includes selected market index ids in metadata update payload', () => {
    const payload = buildUpdateStockMetadataPayload({
      ticker: 'AAPL',
      name: 'Apple Inc.',
      exchange: 'NYSE',
      currentPrice: 101,
      marketIndexIds: [2],
    });

    expect(payload.marketIndexIds).toEqual([2]);
  });
});
