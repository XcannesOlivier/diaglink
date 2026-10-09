import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { clearDiagLinkSession, setDiagLinkSession } from '../../utils/apiAuth';
import { deleteLogo, MAX_COMPANY_LOGO_BYTES, resetBranding, updateAccentColor, uploadLogo } from '../companyBrandingService';

describe('company branding mutations', () => {
  const getAccessToken = vi.fn().mockResolvedValue('test-token');
  beforeEach(() => {
    clearDiagLinkSession();
    getAccessToken.mockClear();
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({ accentColor: null, hasLogo: false })));
  });
  afterEach(() => { clearDiagLinkSession(); vi.restoreAllMocks(); });

  it.each(['#356A9A', null])('patches only the selected accent (%s)', async accentColor => {
    const result = await updateAccentColor(getAccessToken, accentColor);

    expect(result.kind).toBe('success');
    expect(fetch).toHaveBeenCalledWith('/api/company/branding', {
      method: 'PATCH', headers: { Authorization: 'Bearer test-token', 'Content-Type': 'application/json' },
      body: JSON.stringify({ accentColor }),
    });
  });

  it.each(['blue', '#123', '#1234567'])('rejects invalid accent %s without a request', async color => {
    expect((await updateAccentColor(getAccessToken, color)).kind).toBe('validation-error');
    expect(fetch).not.toHaveBeenCalled();
  });

  it.each(['image/png', 'image/jpeg'])('uploads %s using a single multipart logo field', async type => {
    const file = new File(['logo'], 'logo.png', { type });
    await uploadLogo(getAccessToken, file);
    const [url, options] = vi.mocked(fetch).mock.calls[0];
    expect(url).toBe('/api/company/branding/logo');
    expect(options?.method).toBe('PUT');
    expect(options?.headers).toEqual({ Authorization: 'Bearer test-token' });
    expect(options?.body).toBeInstanceOf(FormData);
    if (!(options?.body instanceof FormData)) throw new Error('Expected a multipart body');
    const form = options.body;
    expect([...form.keys()]).toEqual(['logo']);
    expect(form.get('logo')).toBe(file);
  });

  it.each([
    new File(['svg'], 'logo.svg', { type: 'image/svg+xml' }),
    new File([], 'empty.png', { type: 'image/png' }),
    new File([new Uint8Array(MAX_COMPANY_LOGO_BYTES + 1)], 'large.png', { type: 'image/png' }),
  ])('rejects invalid logo $name before sending', async file => {
    expect((await uploadLogo(getAccessToken, file)).kind).toBe('validation-error');
    expect(fetch).not.toHaveBeenCalled();
  });

  it('accepts the exact 2 MiB limit', async () => {
    await uploadLogo(getAccessToken, new File([new Uint8Array(MAX_COMPANY_LOGO_BYTES)], 'limit.jpg', { type: 'image/jpeg' }));
    expect(fetch).toHaveBeenCalledOnce();
  });

  it('uses the session auth helper for delete and full reset', async () => {
    setDiagLinkSession('test-session', '2099-01-01T00:00:00Z');
    await deleteLogo(getAccessToken);
    await resetBranding(getAccessToken);
    expect(fetch).toHaveBeenNthCalledWith(1, '/api/company/branding/logo', {
      method: 'DELETE', headers: { 'X-DiagLink-Session': 'test-session' },
    });
    expect(fetch).toHaveBeenNthCalledWith(2, '/api/company/branding', {
      method: 'DELETE', headers: { 'X-DiagLink-Session': 'test-session' },
    });
    expect(getAccessToken).not.toHaveBeenCalled();
  });

  it.each([
    [400, 'validation-error'], [401, 'unauthorized'], [403, 'forbidden'], [413, 'error'], [500, 'error'],
  ])('maps HTTP %s to %s', async (status, kind) => {
    vi.mocked(fetch).mockResolvedValue(new Response(JSON.stringify({ error: 'server detail' }), { status }));
    expect((await resetBranding(getAccessToken)).kind).toBe(kind);
  });

  it('identifies expired DiagLink sessions', async () => {
    setDiagLinkSession('test-session', '2099-01-01T00:00:00Z');
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 401 }));
    expect(await deleteLogo(getAccessToken)).toEqual({ kind: 'unauthorized', diagLinkSessionExpired: true });
  });

  it('reports network and token failures as errors', async () => {
    vi.mocked(fetch).mockRejectedValue(new TypeError('offline'));
    expect((await resetBranding(getAccessToken)).kind).toBe('error');
    expect((await resetBranding(async () => null)).kind).toBe('error');
  });
});
