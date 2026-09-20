import { afterEach, describe, expect, it, vi } from 'vitest';
import { getWalletTopUps, startWalletTopUp } from '../walletTopUpService';
vi.mock('../../utils/apiAuth', () => ({ getApiAuthHeaders: async () => ({ headers: { Authorization: 'Bearer test' }, mode: 'diaglink' }) }));
describe('Wallet API contract', () => {
  afterEach(() => vi.unstubAllGlobals());
  it('sends operation identity and EUR amount with authentication; reads without writes', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response('{}', { status: 200 })); vi.stubGlobal('fetch', fetchMock);
    await getWalletTopUps(vi.fn(), 'c1'); expect(fetchMock.mock.lastCall?.[1].method).toBe('GET');
    await startWalletTopUp(vi.fn(), 'c1', 'fixed-request', 12.34);
    const [url, init] = fetchMock.mock.lastCall!;
    expect(url).toBe('/api/companies/c1/stripe/wallet-topups'); expect(init.method).toBe('POST');
    expect(init.headers.Authorization).toBe('Bearer test');
    expect(JSON.parse(init.body)).toEqual({ requestId: 'fixed-request', amount: 12.34, currency: 'EUR' });
  });
  it('reports authorization and server validation errors', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response('', { status: 401 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ error: 'Minimum 10 €' }), { status: 400 })); vi.stubGlobal('fetch', fetchMock);
    expect(await getWalletTopUps(vi.fn(), 'c1')).toEqual({ kind: 'unauthorized', diagLinkSessionExpired: true });
    expect(await startWalletTopUp(vi.fn(), 'c1', 'id', 1)).toEqual({ kind: 'validation-error', message: 'Minimum 10 €' });
  });
});
