import React from 'react';
import { Typography } from 'antd';

const { Text } = Typography;

type Props = {
  sector?: string | null;
  industry?: string | null;
};

const CLASSIFICATION_STYLE: React.CSSProperties = {
  marginLeft: 'auto',
  paddingLeft: 12,
  minWidth: 0,
  maxWidth: '55%',
  flex: '0 1 55%',
  textAlign: 'right',
  fontSize: 14,
  lineHeight: '20px',
};

const buildClassificationText = (sector?: string | null, industry?: string | null): string | null => {
  if (sector && industry) {
    return `${sector} · ${industry}`;
  }

  if (sector) {
    return sector;
  }

  return industry ?? null;
};

const StockClassificationBadges: React.FC<Props> = ({ sector, industry }) => {
  const classificationText = buildClassificationText(sector, industry);
  if (!classificationText) {
    return null;
  }

  return (
    <Text
      type="secondary"
      style={CLASSIFICATION_STYLE}
      ellipsis={{ tooltip: classificationText }}
      title={classificationText}
      aria-label={`Классификация: ${classificationText}`}
    >
      {classificationText}
    </Text>
  );
};

export default StockClassificationBadges;
