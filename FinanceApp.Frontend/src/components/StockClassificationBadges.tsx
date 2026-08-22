import React from 'react';
import { Tag, Tooltip } from 'antd';

const SECTOR_COLOR_BY_NAME: Record<string, string> = {
  'Communication Services': 'magenta',
  'Consumer Discretionary': 'orange',
  'Consumer Staples': 'gold',
  Energy: 'volcano',
  Financials: 'geekblue',
  'Health Care': 'green',
  Industrials: 'cyan',
  'Information Technology': 'blue',
  Materials: 'lime',
  'Real Estate': 'purple',
  Utilities: 'default',
};

const denseTagStyle: React.CSSProperties = {
  marginInlineEnd: 0,
  paddingInline: 6,
  lineHeight: '22px',
  fontSize: 16,
};

type Props = {
  sector?: string | null;
  industry?: string | null;
};

const CompactBadge: React.FC<{
  shortLabel: string;
  fullLabel: string;
  color?: string;
}> = ({ shortLabel, fullLabel, color }) => {
  const aria = `${shortLabel}: ${fullLabel}`;
  const tag = (
    <Tag color={color} style={denseTagStyle} title={fullLabel} aria-label={aria}>
      {shortLabel}
    </Tag>
  );
  return (
    <Tooltip title={fullLabel}>
      {tag}
    </Tooltip>
  );
};

const StockClassificationBadges: React.FC<Props> = ({ sector, industry }) => {
  if (!sector && !industry) {
    return null;
  }

  return (
    <div style={{ display: 'inline-flex', gap: 4, flexWrap: 'wrap', alignItems: 'center' }}>
      {sector && (
        <CompactBadge
          shortLabel="СЕК"
          fullLabel={sector}
          color={SECTOR_COLOR_BY_NAME[sector] ?? 'processing'}
        />
      )}
      {industry && (
        <CompactBadge
          shortLabel="ОТР"
          fullLabel={industry}
          color="default"
        />
      )}
    </div>
  );
};

export default StockClassificationBadges;
