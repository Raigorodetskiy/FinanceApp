import React from 'react';
import { Tag, Tooltip } from 'antd';
import type { StockExchange } from '../types';

export const EXCHANGE_ABBREVIATION: Record<StockExchange, string> = {
  NYSE: 'NYSE',
  Frankfurt: 'FRA',
};

export const EXCHANGE_FULL_NAME: Record<StockExchange, string> = {
  NYSE: 'NYSE',
  Frankfurt: 'Frankfurt',
};

type StockExchangeTagProps = {
  exchange: StockExchange;
  style?: React.CSSProperties;
};

/**
 * Renders a compact exchange marker (e.g. "NYSE" or "FRA") with a tooltip
 * showing the full exchange name. Use this component consistently across all
 * pages to avoid duplicating exchange label maps.
 */
const StockExchangeTag: React.FC<StockExchangeTagProps> = ({ exchange, style }) => (
  <Tooltip title={EXCHANGE_FULL_NAME[exchange]}>
    <Tag style={{ marginInlineEnd: 0, ...style }}>{EXCHANGE_ABBREVIATION[exchange]}</Tag>
  </Tooltip>
);

export default StockExchangeTag;

/**
 * Renders ticker + exchange tag side by side.
 */
type StockIdentityProps = {
  ticker: string;
  exchange: StockExchange;
};

export const StockIdentity: React.FC<StockIdentityProps> = ({ ticker, exchange }) => (
  <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
    <span>{ticker}</span>
    <StockExchangeTag exchange={exchange} />
  </span>
);
