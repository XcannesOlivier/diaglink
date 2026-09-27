import { afterEach, describe, expect, it, vi } from 'vitest';
import { clearDiagLinkSession, setDiagLinkSession } from '../../utils/apiAuth';
import { submitSupportContact } from '../supportContactApi';

afterEach(() => {
  clearDiagLinkSession();
  vi.restoreAllMocks();
});

describe('submitSupportContact', () => {
  it('posts only the message and optional machine id with Microsoft authentication', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ success: true }), { status: 200 }));

    const result = await submitSupportContact(async () => 'access-token', {
      message: 'Besoin d’aide.',
      machineId: 'machine-42',
    });

    expect(result).toEqual({ kind: 'success', data: { success: true } });
    expect(fetchMock).toHaveBeenCalledWith('/api/contact', {
      method: 'POST',
      headers: { Authorization: 'Bearer access-token', 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: 'Besoin d’aide.', machineId: 'machine-42' }),
    });
  });

  it('uses the DiagLink session without requesting a Microsoft token', async () => {
    setDiagLinkSession('session-token', '2099-01-01T00:00:00Z');
    const getAccessToken = vi.fn();
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ success: true }), { status: 200 }));

    await submitSupportContact(getAccessToken, { message: 'Besoin d’aide.' });

    expect(getAccessToken).not.toHaveBeenCalled();
    expect(fetchMock).toHaveBeenCalledWith('/api/contact', expect.objectContaining({
      headers: { 'X-DiagLink-Session': 'session-token', 'Content-Type': 'application/json' },
    }));
  });

  it('maps session expiry and inaccessible machines to typed outcomes', async () => {
    setDiagLinkSession('session-token', '2099-01-01T00:00:00Z');
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(null, { status: 404 }));

    await expect(submitSupportContact(async () => null, { message: 'Premier message' }))
      .resolves.toEqual({ kind: 'unauthorized', diagLinkSessionExpired: true });
    await expect(submitSupportContact(async () => null, { message: 'Second message' }))
      .resolves.toEqual({ kind: 'not-found' });
  });
});