import { describe, expect, it } from 'vitest';
import { EXCHANGE_ABBREVIATION, EXCHANGE_FULL_NAME } from '../components/StockExchangeTag';

describe('StockExchangeTag – shared exchange helpers', () => {
  it('renders NYSE abbreviation as NYSE', () => {
    expect(EXCHANGE_ABBREVIATION['NYSE']).toBe('NYSE');
  });

  it('renders Frankfurt abbreviation as FRA', () => {
    expect(EXCHANGE_ABBREVIATION['Frankfurt']).toBe('FRA');
  });

  it('has full name for NYSE', () => {
    expect(EXCHANGE_FULL_NAME['NYSE']).toBe('NYSE');
  });

  it('has full name for Frankfurt', () => {
    expect(EXCHANGE_FULL_NAME['Frankfurt']).toBe('Frankfurt');
  });

  it('covers all StockExchange values in abbreviation map', () => {
    const exchanges = ['NYSE', 'Frankfurt'] as const;
    for (const ex of exchanges) {
      expect(EXCHANGE_ABBREVIATION[ex]).toBeTruthy();
      expect(EXCHANGE_FULL_NAME[ex]).toBeTruthy();
    }
  });
});
