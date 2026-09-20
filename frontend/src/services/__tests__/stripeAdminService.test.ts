import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { addStripeMachine, getStripeAdditions, stripeCompanyRequest } from '../stripeAdminService';
vi.mock('../../utils/apiAuth', () => ({ getApiAuthHeaders: async () => ({ headers: { Authorization: 'Bearer local-test' }, mode: 'microsoft' }) }));
describe('Stripe admin HTTP contract', () => {
  const token = vi.fn(); const fetchMock = vi.fn();
  beforeEach(() => { vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset(); fetchMock.mockResolvedValue(new Response('{}', { status: 200 })); });
  afterEach(() => vi.unstubAllGlobals());
  it('uses authenticated GET for reads and POST without financial inputs for commands', async () => {
    await stripeCompanyRequest(token, 'company');
    expect(fetchMock).toHaveBeenLastCalledWith('/api/companies/company/stripe', { method: 'GET', headers: { Authorization: 'Bearer local-test' } });
    await stripeCompanyRequest(token, 'company', 'customer');
    expect(fetchMock.mock.lastCall?.[0]).toBe('/api/companies/company/stripe/customer');
    await stripeCompanyRequest(token, 'company', 'subscription');
    expect(fetchMock.mock.lastCall?.[0]).toBe('/api/companies/company/stripe/subscription');
    await getStripeAdditions(token, 'company'); expect(fetchMock.mock.lastCall?.[1].method).toBe('GET');
    await addStripeMachine(token, 'company', 'machine/id');
    expect(fetchMock).toHaveBeenLastCalledWith('/api/companies/company/stripe/machine-additions/machine%2Fid',
      { method: 'POST', headers: { Authorization: 'Bearer local-test' } });
  });
  it('preserves authorization and actionable conflict responses', async () => {
    fetchMock.mockResolvedValueOnce(new Response('', { status: 403 }));
    expect(await addStripeMachine(token, 'c1', 'm1')).toEqual({ kind: 'forbidden' });
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ error: 'Réconciliation requise' }), { status: 409 }));
    expect(await addStripeMachine(token, 'c1', 'm1')).toEqual({ kind: 'conflict', message: 'Réconciliation requise' });
  });
});
