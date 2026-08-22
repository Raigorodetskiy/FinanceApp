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
      sectorId: 2,
      marketIndexIds: [1, 3],
    });

    expect(payload.marketIndexIds).toEqual([1, 3]);
    expect(payload.sectorId).toBe(2);
  });

  it('includes selected market index ids in metadata update payload', () => {
    const payload = buildUpdateStockMetadataPayload({
      ticker: 'AAPL',
      name: 'Apple Inc.',
      exchange: 'NYSE',
      currentPrice: 101,
      sectorId: 4,
      marketIndexIds: [2],
    });

    expect(payload.marketIndexIds).toEqual([2]);
    expect(payload.sectorId).toBe(4);
  });

  it('serializes classification clearing as nulls', () => {
    const payload = buildCreateStockPayload({
      ticker: 'AAPL',
      name: 'Apple Inc.',
      exchange: 'NYSE',
      currentPrice: 101,
      sectorId: undefined,
      industryId: undefined,
    });

    expect(payload.sectorId).toBeNull();
    expect(payload.industryId).toBeNull();
  });
});
