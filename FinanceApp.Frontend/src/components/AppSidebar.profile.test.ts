import { describe, expect, it } from 'vitest';
import { PROFILE_ROUTE } from './AppSidebar';

describe('AppSidebar profile action', () => {
  it('navigates to protected profile route', () => {
    expect(PROFILE_ROUTE).toBe('/profile');
  });
});
