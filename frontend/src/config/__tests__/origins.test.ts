import { describe, expect, it } from 'vitest';
import {
  APP_INSTALL_URL,
  APP_LOGIN_URL,
  APP_ORIGIN,
  getEntraRedirectOrigin,
  PUBLIC_ORIGIN,
} from '../origins';

describe('production origins', () => {
  it('keeps public and application URLs centralized', () => {
    expect(PUBLIC_ORIGIN).toBe('https://diaglink.com');
    expect(APP_ORIGIN).toBe('https://app.diaglink.com');
    expect(APP_LOGIN_URL).toBe('https://app.diaglink.com/login');
    expect(APP_INSTALL_URL).toBe('https://app.diaglink.com/login?install=1');
  });

  it('uses the application origin in production and the current origin in development', () => {
    expect(getEntraRedirectOrigin(false, 'http://localhost:5173')).toBe(APP_ORIGIN);
    expect(getEntraRedirectOrigin(true, 'http://localhost:5173')).toBe('http://localhost:5173');
  });
});
