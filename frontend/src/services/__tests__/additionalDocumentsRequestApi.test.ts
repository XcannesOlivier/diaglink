import { beforeEach, describe, expect, it, vi } from 'vitest';
import { cancelAdditionalDocumentsRequest, getAdditionalDocumentsPayment, stageAdditionalDocuments, startAdditionalDocumentsPayment } from '../additionalDocumentsRequestApi';

describe('AdditionalDocuments company API', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('stages only documents with the stable key and selected machine route', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({ requestId: 'r1' }), { status: 201 }));
    const file = new File(['pdf'], 'manual.pdf', { type: 'application/pdf' });
    await stageAdditionalDocuments(async () => 'token', 'machine/id', [file], 'stable-key');
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/company/machines/machine%2Fid/document-requests');
    expect(init?.method).toBe('POST'); expect((init?.headers as Record<string, string>)['Idempotency-Key']).toBe('stable-key');
    const form = init?.body as FormData;
    expect([...form.keys()]).toEqual(['documents']);
    expect(form.has('companyId')).toBe(false); expect(form.has('totalPages')).toBe(false); expect(form.has('amountCents')).toBe(false);
  });

  it('uses separate POST checkout and GET reconciliation routes without request data', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => new Response(JSON.stringify({ status: 'pending' }), { status: 200 }));
    await startAdditionalDocumentsPayment(async () => 'token', 'req/1');
    await getAdditionalDocumentsPayment(async () => 'token', 'req/1');
    expect(fetchMock.mock.calls[0][0]).toBe('/api/company/document-requests/req%2F1/payment');
    expect(fetchMock.mock.calls[0][1]).toMatchObject({ method: 'POST' });
    expect(fetchMock.mock.calls[1][0]).toBe('/api/company/document-requests/req%2F1/payment');
    expect(fetchMock.mock.calls[1][1]).not.toHaveProperty('method');
  });

  it('uses the dedicated durable cancellation route', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      paymentRequestId: 'req/1', paymentStatus: 'cancelled', requestStatus: 'rejected',
    }), { status: 200 }));
    await cancelAdditionalDocumentsRequest(async () => 'token', 'req/1');
    expect(fetchMock).toHaveBeenCalledWith('/api/company/document-requests/req%2F1/cancel', {
      method: 'POST', headers: expect.objectContaining({ Authorization: 'Bearer token' }),
    });
  });
});
