import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createCompanyMachineRequestPayment, getCompanyMachineRequestPayment, submitCompanyMachineRequest } from '../companyMachineRequestPaymentApi';

vi.mock('../../utils/apiAuth', () => ({
  getApiAuthHeaders: async () => ({ headers: { Authorization: 'Bearer test' }, mode: 'microsoft' }),
}));

describe('companyMachineRequestPaymentApi', () => {
  beforeEach(() => vi.stubGlobal('fetch', vi.fn()));

  it('sends only documents with the stable idempotency key', async () => {
    const payment = { paymentRequestId: 'p1', status: 'pending', amount: 129.8, currency: 'EUR', checkoutUrl: 'https://checkout.stripe.test' };
    vi.mocked(fetch).mockResolvedValue(new Response(JSON.stringify(payment), { status: 200 }));
    const document = new File(['pdf'], 'manual.pdf', { type: 'application/pdf' });

    await createCompanyMachineRequestPayment(async () => 'token', [document], 'stable-uuid');

    const [url, init] = vi.mocked(fetch).mock.calls[0];
    expect(url).toBe('/api/company/machine-request-payments');
    expect(init?.headers).toEqual({ Authorization: 'Bearer test', 'Idempotency-Key': 'stable-uuid' });
    const form = init?.body as FormData;
    expect(form.getAll('documents')).toEqual([document]);
    expect([...form.keys()]).toEqual(['documents']);
  });

  it('polls the authenticated owner endpoint without exposing an identifier in a message', async () => {
    vi.mocked(fetch).mockResolvedValue(new Response(JSON.stringify({ paymentRequestId: 'p1', status: 'authorized', amount: 129.8, currency: 'EUR' }), { status: 200 }));
    await getCompanyMachineRequestPayment(async () => 'token', 'p1');
    expect(fetch).toHaveBeenCalledWith('/api/company/machine-request-payments/p1', { headers: { Authorization: 'Bearer test' } });
  });

  it('submits payment, machine fields and documents without browser-owned identity or pricing', async () => {
    vi.mocked(fetch).mockResolvedValue(new Response(JSON.stringify({ requestId: 'r1', status: 'pending' }), { status: 201 }));
    const document = new File(['pdf'], 'manual.pdf', { type: 'application/pdf' });
    await submitCompanyMachineRequest(async () => 'token', 'payment-1', {
      machineName: 'Machine', manufacturer: 'Atlas', model: 'H23', serialNumber: 'S1', description: 'Test',
    }, [document]);
    const [url, init] = vi.mocked(fetch).mock.calls[0];
    expect(url).toBe('/api/company/machine-requests');
    const form = init?.body as FormData;
    expect(Object.fromEntries(form.entries())).toMatchObject({ paymentRequestId: 'payment-1', machineName: 'Machine', manufacturer: 'Atlas', model: 'H23', serialNumber: 'S1', description: 'Test', documents: document });
    for (const forbidden of ['companyId', 'userId', 'requestKind', 'amount', 'totalPages']) expect(form.has(forbidden)).toBe(false);
  });
});
