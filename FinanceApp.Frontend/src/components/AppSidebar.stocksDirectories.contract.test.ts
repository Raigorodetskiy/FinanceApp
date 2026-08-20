import { describe, expect, it } from 'vitest';
import React from 'react';
import {
  STOCKS_DIRECTORIES_PARENT_ICON,
  STOCKS_DIRECTORIES_PARENT_KEY,
  STOCKS_DIRECTORIES_PARENT_LABEL,
  STOCKS_DIRECTORIES_MENU_ENTRIES,
  buildStocksDirectoriesMenuItems,
} from './AppSidebar';

describe('AppSidebar stocks-directories items contract', () => {
  it('keeps parent submenu key/label and icon element', () => {
    expect(STOCKS_DIRECTORIES_PARENT_KEY).toBe('stocks-directories');
    expect(STOCKS_DIRECTORIES_PARENT_LABEL).toBe('Справочники');
    expect(React.isValidElement(STOCKS_DIRECTORIES_PARENT_ICON)).toBe(true);
  });

  it('keeps keys/routes unchanged and provides icon React elements', () => {
    expect(STOCKS_DIRECTORIES_MENU_ENTRIES).toMatchObject([
      { key: 'sectors', route: '/sectors', label: 'Секторы и отрасли' },
      { key: 'financial-metrics', route: '/financial-metrics', label: 'Финансовые показатели' },
      { key: 'help', route: '/help', label: 'Справка' },
    ]);

    for (const entry of STOCKS_DIRECTORIES_MENU_ENTRIES) {
      expect(React.isValidElement(entry.icon)).toBe(true);
    }
  });

  it('builds submenu items with icons and forwards routes on click', () => {
    const navigatedRoutes: string[] = [];
    const items = buildStocksDirectoriesMenuItems((route) => navigatedRoutes.push(route));

    expect(items).toHaveLength(3);
    for (let index = 0; index < items.length; index += 1) {
      const item = items[index];
      const entry = STOCKS_DIRECTORIES_MENU_ENTRIES[index];
      expect(item?.key).toBe(entry.key);
      expect(item?.label).toBe(entry.label);
      expect(React.isValidElement(item?.icon as React.ReactNode)).toBe(true);
      (item?.onClick as (() => void) | undefined)?.();
    }

    expect(navigatedRoutes).toEqual(['/sectors', '/financial-metrics', '/help']);
  });
});
