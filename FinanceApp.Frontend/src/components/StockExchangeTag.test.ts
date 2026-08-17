import { describe, expect, it } from 'vitest';
import { EXCHANGE_ABBREVIATION, EXCHANGE_FULL_NAME } from './StockExchangeTag';

describe('StockExchangeTag exchange maps', () => {
  it('supports NASDAQ alongside NYSE and Frankfurt', () => {
    expect(EXCHANGE_ABBREVIATION.NYSE).toBe('NYSE');
    expect(EXCHANGE_ABBREVIATION.NASDAQ).toBe('NASDAQ');
    expect(EXCHANGE_ABBREVIATION.Frankfurt).toBe('FRA');

    expect(EXCHANGE_FULL_NAME.NYSE).toBe('NYSE');
    expect(EXCHANGE_FULL_NAME.NASDAQ).toBe('NASDAQ');
    expect(EXCHANGE_FULL_NAME.Frankfurt).toBe('Frankfurt');
  });
});
