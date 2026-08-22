import React from 'react';
import { Typography } from 'antd';

const { Text } = Typography;

type Props = {
  sector?: string | null;
  industry?: string | null;
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
      style={{ display: 'block', fontSize: 16, lineHeight: 1.2 }}
      ellipsis={{ tooltip: classificationText }}
      title={classificationText}
      aria-label={`Классификация: ${classificationText}`}
    >
      {classificationText}
    </Text>
  );
};

export default StockClassificationBadges;
