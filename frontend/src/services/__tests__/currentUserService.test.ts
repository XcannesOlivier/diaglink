import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fetchCurrentUser } from '../currentUserService';

describe('fetchCurrentUser branding', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('parses companyBranding from /api/auth/me', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      userId: 'u1',
      companyId: 'c1',
      role: 'company_admin',
      companyBranding: {
        companyName: 'Acme',
        accentColor: '#123456',
        hasLogo: true,
        logoVersion: '638956388960000000',
      },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })));

    const result = await fetchCurrentUser(async () => 'token');

    expect(result.currentUser?.companyBranding).toEqual({
      companyName: 'Acme',
      accentColor: '#123456',
      hasLogo: true,
      logoVersion: '638956388960000000',
    });
  });

  it('preserves an absent branding property and a null super-admin branding', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ userId: 'u1', companyId: 'c1', role: 'technician' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        userId: 'sa', companyId: 'root', role: 'diaglink_super_admin', companyBranding: null,
      }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    const absent = await fetchCurrentUser(async () => 'token');
    const superAdmin = await fetchCurrentUser(async () => 'token');

    expect(absent.currentUser?.companyBranding).toBeUndefined();
    expect(superAdmin.currentUser?.companyBranding).toBeNull();
  });
});
