import { afterEach, describe, expect, it, vi } from 'vitest';
import { buildMachineRequestFormData, MachineRequestSubmissionError, submitMachineRequest } from '../machineRequestApi';
import type { StartFormValues } from '../../pages/start/startFormValidation';

const values: StartFormValues = {
  firstName: 'Claire', lastName: 'Martin', company: 'Ateliers Martin',
  email: 'claire@example.com', phone: '+33 6 12 34 56 78',
  machineName: 'Compresseur', manufacturer: 'Atlas Copco', model: 'GA90',
  serialNumber: 'SN-42', description: 'Machine principale',
};

afterEach(() => vi.unstubAllGlobals());

describe('machine request API', () => {
  it('builds the expected multipart fields without client pricing values', () => {
    const first = new File(['pdf-1'], 'manual.pdf', { type: 'application/pdf' });
    const second = new File(['pdf-2'], 'schema.pdf', { type: 'application/pdf' });
    const formData = buildMachineRequestFormData(values, [first, second], 'payment-42');

    expect(Object.fromEntries([...formData.entries()].filter(([, value]) => typeof value === 'string'))).toEqual({
      paymentRequestId: 'payment-42',
      ...values,
    });
    expect(formData.getAll('documents')).toEqual([first, second]);
    expect(formData.get('paymentRequestId')).toBe('payment-42');
    expect(formData.has('totalPages')).toBe(false);
    expect(formData.has('preparationTotal')).toBe(false);
  });

  it('posts FormData and returns the server response', async () => {
    const serverResponse = { requestId: 'request-42', status: 'pending', createdAt: '2026-09-22T10:00:00Z', documentCount: 1, totalPages: 777, additionalPages: 377, preparationTotal: 201.69 };
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(serverResponse), { status: 201, headers: { 'Content-Type': 'application/json' } }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(submitMachineRequest(values, [new File(['pdf'], 'manual.pdf')], 'payment-42')).resolves.toEqual(serverResponse);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith('/api/public/machine-requests', expect.objectContaining({ method: 'POST', body: expect.any(FormData) }));
    expect((fetchMock.mock.calls[0][1] as RequestInit).headers).toBeUndefined();
  });

  it('exposes a safe backend validation message', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ errors: { documents: ['Le PDF est invalide.'] } }), { status: 400 })));
    await expect(submitMachineRequest(values, [], 'payment-42')).rejects.toMatchObject({ message: 'Le PDF est invalide.', isNetworkError: false });
  });

  it('maps a network failure to the public retry message', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('private network detail')));
    await expect(submitMachineRequest(values, [], 'payment-42')).rejects.toEqual(expect.objectContaining<Partial<MachineRequestSubmissionError>>({
      message: 'Impossible de contacter le service pour le moment. Veuillez réessayer.',
      isNetworkError: true,
    }));
  });
});
