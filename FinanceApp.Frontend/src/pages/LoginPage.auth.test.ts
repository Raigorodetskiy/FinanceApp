import { describe, expect, it } from 'vitest';
import { LOGIN_IDENTIFIER_LABEL, toLoginPayload } from './LoginPage';

describe('LoginPage auth payload', () => {
  it('uses combined login/email label', () => {
    expect(LOGIN_IDENTIFIER_LABEL).toBe('Логин или email');
  });

  it('sends identifier payload for login', () => {
    expect(toLoginPayload({ identifier: ' user ', password: 'secret' })).toEqual({
      identifier: ' user ',
      password: 'secret',
    });
  });
});
