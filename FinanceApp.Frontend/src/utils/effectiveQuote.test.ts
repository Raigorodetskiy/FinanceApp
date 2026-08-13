import { describe, expect, it } from 'vitest';
import type { PortfolioItem, Stock } from '../types';
import {
  parseUtcTimestamp,
  resolveEffectiveQuote,
  stocksMatch,
} from './effectiveQuote';

const NOW = Date.parse('2026-08-13T20:54:11Z');

const makeStock = (overrides: Partial<Stock> & Pick<Stock, 'id'>): Stock => ({
  id: overrides.id,
  ticker: overrides.ticker ?? `T${overrides.id}`,
  name: overrides.name ?? `Stock ${overrides.id}`,
  commonName: overrides.commonName ?? 'Shared Name',
  exchange: overrides.exchange ?? 'NYSE',
  currentPrice: overrides.currentPrice ?? 100,
  currentPriceChange: overrides.currentPriceChange ?? null,
  currentPriceChangePercent: overrides.currentPriceChangePercent ?? null,
  currentPriceAt:
    overrides.currentPriceAt !== undefined ? overrides.currentPriceAt : '2026-08-13T20:00:00Z',
  updatedAt: overrides.updatedAt ?? '2026-08-13T20:00:00Z',
  isin: overrides.isin ?? 'IE00BKVD2N49',
});

const makePortfolioItem = (
  stock: Stock,
  overrides: Partial<PortfolioItem> = {},
): PortfolioItem => ({
  id: overrides.id ?? 101,
  portfolioId: overrides.portfolioId ?? 1,
  stockId: overrides.stockId ?? stock.id,
  stock,
  quantity: overrides.quantity ?? 1,
  buyPrice: overrides.buyPrice ?? 100,
  boughtAt: overrides.boughtAt ?? '2026-08-01T00:00:00Z',
});

describe('resolveEffectiveQuote – newest valid quote wins', () => {
  it('Seagate fixture: newer NYSE quote wins over older Frankfurt quote', () => {
    const frankfurt = makeStock({
      id: 8,
      ticker: '847',
      exchange: 'Frankfurt',
      name: 'Seagate Technology Holdings PLC',
      commonName: 'Seagate Technology Holdings PLC',
      currentPrice: 756,
      currentPriceAt: '2026-08-13T06:00:59',
    });
    const nyse = makeStock({
      id: 54,
      ticker: 'STX',
      exchange: 'NYSE',
      name: 'Seagate Technology Holdings PLC',
      commonName: 'Seagate Technology Holdings PLC',
      currentPrice: 798.83,
      currentPriceAt: '2026-08-13T20:00:00',
    });

    const eq = resolveEffectiveQuote(frankfurt, [frankfurt, nyse], NOW);

    expect(eq.currentPrice).toBe(798.83);
    expect(eq.currentPriceAt).toBe('2026-08-13T20:00:00');
    expect(eq.sourceStockId).toBe(54);
    expect(eq.sourceExchange).toBe('NYSE');
  });

  it('keeps primary when primary timestamp is newer', () => {
    const primary = makeStock({ id: 1, currentPrice: 500, currentPriceAt: '2026-08-13T20:10:00Z' });
    const alt = makeStock({
      id: 2,
      currentPrice: 700,
      currentPriceAt: '2026-08-13T20:09:59.999Z',
      exchange: 'Frankfurt',
    });

    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);

    expect(eq.currentPrice).toBe(500);
    expect(eq.sourceStockId).toBeNull();
    expect(eq.sourceExchange).toBeNull();
  });

  it('uses alternative when it is newer even by 1 ms', () => {
    const primary = makeStock({ id: 1, currentPrice: 500, currentPriceAt: '2026-08-13T20:10:00.000Z' });
    const alt = makeStock({
      id: 2,
      currentPrice: 501,
      currentPriceAt: '2026-08-13T20:10:00.001Z',
      exchange: 'Frankfurt',
    });

    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);

    expect(eq.currentPrice).toBe(501);
    expect(eq.sourceStockId).toBe(2);
    expect(eq.sourceExchange).toBe('Frankfurt');
  });

  it('uses primary on equal timestamps', () => {
    const primary = makeStock({ id: 1, currentPrice: 100, currentPriceAt: '2026-08-13T20:00:00Z' });
    const alt = makeStock({ id: 2, currentPrice: 999, currentPriceAt: '2026-08-13T20:00:00Z', exchange: 'Frankfurt' });

    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);

    expect(eq.currentPrice).toBe(100);
    expect(eq.sourceStockId).toBeNull();
  });

  it('uses lower Stock.id when top timestamp tie is between alternatives and primary is older', () => {
    const primary = makeStock({ id: 10, currentPrice: 10, currentPriceAt: '2026-08-13T19:00:00Z' });
    const altA = makeStock({ id: 5, currentPrice: 200, currentPriceAt: '2026-08-13T20:00:00Z', exchange: 'Frankfurt' });
    const altB = makeStock({ id: 7, currentPrice: 300, currentPriceAt: '2026-08-13T20:00:00Z', exchange: 'NYSE' });

    const eq = resolveEffectiveQuote(primary, [primary, altB, altA], NOW);

    expect(eq.currentPrice).toBe(200);
    expect(eq.sourceStockId).toBe(5);
    expect(eq.sourceExchange).toBe('Frankfurt');
  });

  it('selects newer quote even when both timestamps are old', () => {
    const primary = makeStock({ id: 1, currentPrice: 100, currentPriceAt: '2026-08-01T10:00:00Z' });
    const alt = makeStock({
      id: 2,
      currentPrice: 120,
      currentPriceAt: '2026-08-03T10:00:00Z',
      exchange: 'Frankfurt',
    });

    const eq = resolveEffectiveQuote(primary, [primary, alt], NOW);

    expect(eq.currentPrice).toBe(120);
    expect(eq.sourceStockId).toBe(2);
  });

  it('excludes future timestamp candidates', () => {
    const primary = makeStock({ id: 1, currentPrice: 100, currentPriceAt: '2026-08-13T20:00:00Z' });
    const altFuture = makeStock({
      id: 2,
      currentPrice: 500,
      currentPriceAt: '2026-08-13T21:00:00Z',
      exchange: 'Frankfurt',
    });

    const eq = resolveEffectiveQuote(primary, [primary, altFuture], NOW);

    expect(eq.currentPrice).toBe(100);
    expect(eq.sourceStockId).toBeNull();
  });

  it('excludes invalid and null timestamps', () => {
    const primary = makeStock({ id: 1, currentPrice: 100, currentPriceAt: '2026-08-13T20:00:00Z' });
    const invalid = makeStock({ id: 2, currentPrice: 400, currentPriceAt: 'not-a-date', exchange: 'Frankfurt' });
    const missing = makeStock({ id: 3, currentPrice: 600, currentPriceAt: null, exchange: 'Frankfurt' });

    const eq = resolveEffectiveQuote(primary, [primary, invalid, missing], NOW);

    expect(eq.currentPrice).toBe(100);
    expect(eq.sourceStockId).toBeNull();
  });

  it('falls back to primary stored price when no valid timestamp exists', () => {
    const primary = makeStock({ id: 1, currentPrice: 111, currentPriceAt: null });
    const invalid = makeStock({ id: 2, currentPrice: 222, currentPriceAt: 'bad', exchange: 'Frankfurt' });
    const future = makeStock({ id: 3, currentPrice: 333, currentPriceAt: '3026-08-13T20:00:00Z', exchange: 'Frankfurt' });

    const eq = resolveEffectiveQuote(primary, [primary, invalid, future], NOW);

    expect(eq.currentPrice).toBe(111);
    expect(eq.currentPriceAt).toBeNull();
    expect(eq.sourceStockId).toBeNull();
    expect(eq.sourceExchange).toBeNull();
  });

  it('preserves PortfolioItem identity when applying effective quote', () => {
    const primary = makeStock({
      id: 8,
      ticker: '847',
      exchange: 'Frankfurt',
      name: 'Seagate Technology Holdings PLC',
      commonName: 'Seagate Technology Holdings PLC',
      currentPrice: 756,
      currentPriceAt: '2026-08-13T06:00:59',
    });
    const nyse = makeStock({
      id: 54,
      ticker: 'STX',
      exchange: 'NYSE',
      name: 'Seagate Technology Holdings PLC',
      commonName: 'Seagate Technology Holdings PLC',
      currentPrice: 798.83,
      currentPriceAt: '2026-08-13T20:00:00',
    });

    const item = makePortfolioItem(primary, { stockId: primary.id });
    const eq = resolveEffectiveQuote(primary, [primary, nyse], NOW);

    const effectiveItem = {
      ...item,
      stock: {
        ...item.stock,
        currentPrice: eq.currentPrice,
        currentPriceAt: eq.currentPriceAt,
      },
    };

    expect(effectiveItem.stockId).toBe(primary.id);
    expect(effectiveItem.stock.id).toBe(primary.id);
    expect(effectiveItem.stock.ticker).toBe('847');
    expect(eq.sourceStockId).toBe(54);
  });

  it('diagnostics report raw/utc timestamp, statuses and selection reason without stale-window wording', () => {
    const primary = makeStock({ id: 1, currentPriceAt: null, currentPrice: 100 });
    const invalid = makeStock({ id: 2, currentPriceAt: 'bad', currentPrice: 200, exchange: 'Frankfurt' });
    const future = makeStock({
      id: 3,
      currentPriceAt: '2026-08-13T21:54:11Z',
      currentPrice: 300,
      exchange: 'Frankfurt',
    });

    const eq = resolveEffectiveQuote(primary, [primary, invalid, future], NOW);

    expect(eq.diagnosticInfo).toContain('raw=""');
    expect(eq.diagnosticInfo).toContain('utc="n/a"');
    expect(eq.diagnosticInfo).toContain('status=invalid (no timestamp)');
    expect(eq.diagnosticInfo).toContain('status=invalid (unparseable)');
    expect(eq.diagnosticInfo).toContain('status=future');
    expect(eq.diagnosticInfo).toContain('fallback to primary stored price (no valid candidate timestamp)');
    expect(eq.diagnosticInfo).not.toContain('fresh');
    expect(eq.diagnosticInfo).not.toContain('stale');
  });
});

describe('timestamp parsing and matching utilities', () => {
  it('treats timezone-less timestamp as UTC', () => {
    expect(parseUtcTimestamp('2026-08-13T20:00:00')).toBe(
      Date.parse('2026-08-13T20:00:00Z'),
    );
  });

  it('stocksMatch stays strict and deterministic', () => {
    const a = makeStock({ id: 1, commonName: 'Seagate Technology Holdings PLC', name: 'Name A' });
    const b = makeStock({ id: 2, commonName: ' seagate technology holdings plc ', name: 'Name B' });
    const c = makeStock({ id: 3, commonName: '', name: 'Different Company' });

    expect(stocksMatch(a, b)).toBe(true);
    expect(stocksMatch(a, c)).toBe(false);
  });
});
