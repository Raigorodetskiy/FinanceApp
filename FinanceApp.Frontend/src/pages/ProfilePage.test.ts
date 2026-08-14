import { describe, expect, it } from 'vitest';
import { PROFILE_PASSWORD_LOGOUT_MESSAGE, PROFILE_ROUTE_KEY } from './ProfilePage';

describe('ProfilePage settings behavior constants', () => {
  it('uses profile sidebar selection key', () => {
    expect(PROFILE_ROUTE_KEY).toBe('profile');
  });

  it('shows logout warning for successful password change', () => {
    expect(PROFILE_PASSWORD_LOGOUT_MESSAGE).toContain('разлогинены');
  });
});
