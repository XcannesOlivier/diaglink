import { afterEach, describe, expect, it, vi } from 'vitest';
import { createMachineRequestPayment, waitForMachineRequestAuthorization } from '../machineRequestPaymentApi';

afterEach(() => vi.unstubAllGlobals());

describe('machine request payment API', () => {
  it('creates a payment from pages and email only', async () => {
    const payment = { paymentRequestId: 'payment-42', status: 'pending', amount: 99.9, currency: 'EUR', checkoutUrl: 'https://checkout.stripe.test' };
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(payment), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(createMachineRequestPayment(400, 'client@example.com')).resolves.toEqual(payment);
    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string);
    expect(body).toEqual({ totalPages: 400, email: 'client@example.com' });
    expect(body).not.toHaveProperty('amount');
  });

  it('polls pending states until authorization', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ paymentRequestId: 'payment-42', status: 'pending', amount: 99.9, currency: 'EUR' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ paymentRequestId: 'payment-42', status: 'authorized', amount: 99.9, currency: 'EUR' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(waitForMachineRequestAuthorization('payment-42', 0, 2)).resolves.toMatchObject({ status: 'authorized' });
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('stops polling on a terminal unusable status', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      paymentRequestId: 'payment-42', status: 'cancelled', amount: 99.9, currency: 'EUR',
    }), { status: 200 })));
    await expect(waitForMachineRequestAuthorization('payment-42', 0, 2)).rejects.toThrow('ne peut plus être utilisée');
  });
});
