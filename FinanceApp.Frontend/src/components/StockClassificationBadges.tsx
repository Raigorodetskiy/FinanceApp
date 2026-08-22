import React from 'react';
import { Typography } from 'antd';

const { Text } = Typography;

type Props = {
  sector?: string | null;
  industry?: string | null;
};

const KNOWN_SECTOR_CODES: Record<string, string> = {
  'communication services': 'COM',
  'consumer discretionary': 'CD',
  'consumer staples': 'CS',
  energy: 'EN',
  financials: 'FIN',
  'health care': 'HC',
  industrials: 'IND',
  'information technology': 'IT',
  materials: 'MAT',
  'real estate': 'RE',
  utilities: 'UTIL',
};

const COMPACT_FALLBACK_MAX_LENGTH = 4;

const normalizeClassification = (value: string): string => value.trim().replace(/\s+/g, ' ').toLowerCase();

const buildInitialismFallback = (value: string): string | null => {
  const words = value.match(/[A-Za-zА-Яа-я0-9]+/g) ?? [];
  if (words.length === 0) {
    return null;
  }

  if (words.length === 1) {
    return words[0]!.toUpperCase().slice(0, COMPACT_FALLBACK_MAX_LENGTH);
  }

  return words
    .map((word) => word[0]!.toUpperCase())
    .join('')
    .slice(0, COMPACT_FALLBACK_MAX_LENGTH);
};

export const buildClassificationText = (sector?: string | null, industry?: string | null): string | null => {
  if (sector && industry) {
    return `${sector} · ${industry}`;
  }

  if (sector) {
    return sector;
  }

  return industry ?? null;
};

export const buildCompactClassificationCode = (sector?: string | null, industry?: string | null): string | null => {
  const normalizedSector = sector ? normalizeClassification(sector) : null;
  if (normalizedSector) {
    const knownCode = KNOWN_SECTOR_CODES[normalizedSector];
    if (knownCode) {
      return knownCode;
    }
    const sectorFallback = buildInitialismFallback(sector!);
    if (sectorFallback) {
      return sectorFallback;
    }
  }

  if (industry) {
    const industryFallback = buildInitialismFallback(industry);
    if (industryFallback) {
      return industryFallback;
    }
  }

  return null;
};

const StockClassificationBadges: React.FC<Props> = ({ sector, industry }) => {
  const classificationText = buildClassificationText(sector, industry);
  const compactCode = buildCompactClassificationCode(sector, industry);
  if (!classificationText || !compactCode) {
    return null;
  }

  return (
    <Text
      type="secondary"
      style={{
        marginLeft: 'auto',
        maxWidth: '45%',
        minWidth: 0,
        textAlign: 'right',
        fontSize: 14,
        lineHeight: 1.2,
        flex: '0 0 auto',
      }}
      ellipsis={{ tooltip: classificationText }}
      title={classificationText}
      aria-label={`Классификация: ${classificationText}`}
    >
      {compactCode}
    </Text>
  );
};

export default StockClassificationBadges;
