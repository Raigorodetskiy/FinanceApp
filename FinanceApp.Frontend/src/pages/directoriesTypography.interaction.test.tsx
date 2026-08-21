// @vitest-environment jsdom
import { describe, expect, it, beforeEach } from 'vitest';
import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const indexCss = readFileSync(join(__dirname, '..', 'index.css'), 'utf-8');
const directoriesCss = readFileSync(join(__dirname, 'directoriesTypography.css'), 'utf-8');
const helpCss = readFileSync(join(__dirname, 'HelpPage.css'), 'utf-8');

const fontSize = (selector: string) => {
  const element = document.querySelector(selector);
  expect(element).not.toBeNull();
  return Number.parseFloat(window.getComputedStyle(element as Element).fontSize);
};

describe('directories typography computed styles', () => {
  beforeEach(() => {
    document.head.innerHTML = '';
    document.body.innerHTML = '';

    const style = document.createElement('style');
    style.textContent = `${indexCss}\n${directoriesCss}\n${helpCss}`;
    document.head.appendChild(style);

    document.body.innerHTML = `
      <div id="root"></div>
      <div class="help-page directories-typography">
        <p class="ant-typography ant-typography-secondary help-sidebar-description">sidebar</p>
        <input class="ant-input help-search" placeholder="search" />
        <div class="ant-card-head-title help-category-title">category</div>
        <p class="ant-typography help-page__landing-intro">intro <a class="help-quick-link" href="#">quick link</a></p>
        <h2 class="ant-typography help-heading-h2">H2</h2>
        <h3 class="ant-typography help-heading-h3">H3</h3>
        <h4 class="ant-typography help-heading-h4">H4</h4>
        <table class="ant-table">
          <thead><tr><th>header</th></tr></thead>
          <tbody><tr><td>cell</td></tr></tbody>
        </table>
      </div>
      <div class="directories-overlay-typography">
        <div class="ant-modal-title">modal title</div>
        <div class="ant-form-item-label"><label>label</label></div>
        <div class="ant-form-item-explain-error">error</div>
        <div class="ant-select-item-option-content">option</div>
        <div class="ant-popconfirm-title">confirm</div>
        <div class="ant-tooltip-inner">tooltip</div>
      </div>
    `;
  });

  it('keeps global baseline at 16px and enforces 18px minimum in directories scope', () => {
    expect(window.getComputedStyle(document.body).fontSize).toBe('16px');
    expect(window.getComputedStyle(document.getElementById('root') as HTMLElement).fontSize).toBe('16px');

    expect(fontSize('.help-sidebar-description')).toBeGreaterThanOrEqual(18);
    expect(fontSize('.help-search')).toBeGreaterThanOrEqual(18);
    expect(fontSize('.help-category-title')).toBeGreaterThanOrEqual(18);
    expect(fontSize('.help-page__landing-intro')).toBeGreaterThanOrEqual(18);
    expect(fontSize('.help-quick-link')).toBeGreaterThanOrEqual(18);
    expect(fontSize('.ant-table thead th')).toBeGreaterThanOrEqual(18);
    expect(fontSize('.ant-table tbody td')).toBeGreaterThanOrEqual(18);
  });

  it('preserves heading hierarchy above minimum size', () => {
    const h2 = fontSize('.help-heading-h2');
    const h3 = fontSize('.help-heading-h3');
    const h4 = fontSize('.help-heading-h4');

    expect(h2).toBeGreaterThan(h3);
    expect(h3).toBeGreaterThan(h4);
    expect(h4).toBeGreaterThanOrEqual(18);
  });

  it('applies 18px minimum inside overlay typography containers', () => {
    expect(fontSize('.ant-modal-title')).toBeGreaterThanOrEqual(18);
    expect(fontSize('.ant-form-item-label label')).toBeGreaterThanOrEqual(18);
    expect(fontSize('.ant-form-item-explain-error')).toBeGreaterThanOrEqual(18);
    expect(fontSize('.ant-select-item-option-content')).toBeGreaterThanOrEqual(18);
    expect(fontSize('.ant-popconfirm-title')).toBeGreaterThanOrEqual(18);
    expect(fontSize('.ant-tooltip-inner')).toBeGreaterThanOrEqual(18);
  });
});
