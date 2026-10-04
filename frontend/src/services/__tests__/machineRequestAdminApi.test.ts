import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { activateMachineRequest, attachMachineRequestBusinessEntities, cancelMachineRequestPayment, captureMachineRequestPayment, configureMachineRequestSubscription, decideAdditionalDocumentsRequest, decideAdditionalMachineRequest, downloadMachineRequestDocument, getMachineRequest, linkMachineRequestCustomer, listArchivedMachineRequests, listMachineRequests, markMachineRequestReady, updateMachineRequestArchive, updateMachineRequestStatus } from '../machineRequestAdminApi';

const token = vi.fn().mockResolvedValue('access-token');

beforeEach(() => { sessionStorage.clear(); token.mockClear(); });
afterEach(() => vi.unstubAllGlobals());

describe('machine request admin API', () => {
  it('uses the authenticated admin list and detail routes', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response('[]', { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ requestId: 'req-1' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await listMachineRequests(token);
    await getMachineRequest(token, 'req/1');
    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/admin/machine-requests', { headers: { Authorization: 'Bearer access-token' } });
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/admin/machine-requests/req%2F1', { headers: { Authorization: 'Bearer access-token' } });
  });

  it('sends only the requested backend status in the PATCH body', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ requestId: 'req-1', status: 'treated' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await updateMachineRequestStatus(token, 'req-1', 'treated');
    expect(fetchMock).toHaveBeenCalledWith('/api/admin/machine-requests/req-1/status', expect.objectContaining({
      method: 'PATCH', body: JSON.stringify({ status: 'treated' }), headers: { Authorization: 'Bearer access-token', 'Content-Type': 'application/json' },
    }));
  });

  it('uses the Super Admin history and archive routes without changing business status', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ requestId: 'req-1', status: 'treated' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await listArchivedMachineRequests(token);
    await updateMachineRequestArchive(token, 'req/1', true);
    await updateMachineRequestArchive(token, 'req/1', false);
    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/admin/machine-requests/history', { headers: { Authorization: 'Bearer access-token' } });
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/admin/machine-requests/req%2F1/archive', expect.objectContaining({ method: 'PATCH', body: JSON.stringify({ isArchived: true }) }));
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/admin/machine-requests/req%2F1/archive', expect.objectContaining({ method: 'PATCH', body: JSON.stringify({ isArchived: false }) }));
  });

  it('uses the existing authenticated capture and cancel routes', async () => {
    const response = JSON.stringify({ paymentRequestId: 'pay-1', status: 'captured', amount: 99.9, currency: 'EUR' });
    const fetchMock = vi.fn().mockResolvedValue(new Response(response, { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await captureMachineRequestPayment(token, 'pay/1');
    await cancelMachineRequestPayment(token, 'pay/1');

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/admin/machine-request-payments/pay%2F1/capture', {
      method: 'POST', headers: { Authorization: 'Bearer access-token' },
    });
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/admin/machine-request-payments/pay%2F1/cancel', {
      method: 'POST', headers: { Authorization: 'Bearer access-token' },
    });
  });

  it('uses dedicated AdditionalMachine accept and reject routes without client amounts or identifiers', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ requestId: 'req-1' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await decideAdditionalMachineRequest(token, 'req/1', 'accept');
    await decideAdditionalMachineRequest(token, 'req/1', 'reject');
    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/admin/machine-requests/req%2F1/additional-machine/accept', { method: 'POST', headers: { Authorization: 'Bearer access-token' } });
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/admin/machine-requests/req%2F1/additional-machine/reject', { method: 'POST', headers: { Authorization: 'Bearer access-token' } });
  });

  it('uses dedicated AdditionalDocuments accept and reject routes without client amounts or identifiers', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ requestId: 'req-1' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await decideAdditionalDocumentsRequest(token, 'req/1', 'accept');
    await decideAdditionalDocumentsRequest(token, 'req/1', 'reject');
    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/admin/machine-requests/req%2F1/additional-documents/accept', { method: 'POST', headers: { Authorization: 'Bearer access-token' } });
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/admin/machine-requests/req%2F1/additional-documents/reject', { method: 'POST', headers: { Authorization: 'Bearer access-token' } });
  });

  it('sends only the selected company and machine to the provisioning route', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ requestId: 'req-1' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await attachMachineRequestBusinessEntities(token, 'req/1', 'company-1', 'machine-1');

    expect(fetchMock).toHaveBeenCalledWith('/api/admin/machine-requests/req%2F1/provisioning/business-entities', {
      method: 'PATCH',
      headers: { Authorization: 'Bearer access-token', 'Content-Type': 'application/json' },
      body: JSON.stringify({ companyId: 'company-1', machineId: 'machine-1' }),
    });
  });

  it('starts customer provisioning without sending Stripe identifiers', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ requestId: 'req-1' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await linkMachineRequestCustomer(token, 'req/1');
    expect(fetchMock).toHaveBeenCalledWith('/api/admin/machine-requests/req%2F1/provisioning/customer', {
      method: 'POST', headers: { Authorization: 'Bearer access-token' },
    });
  });

  it('configures the subscription using only the request identifier', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ requestId: 'req-1' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await configureMachineRequestSubscription(token, 'req/1');
    expect(fetchMock).toHaveBeenCalledWith('/api/admin/machine-requests/req%2F1/provisioning/subscription', {
      method: 'POST', headers: { Authorization: 'Bearer access-token' },
    });
  });

  it('activates provisioning using only the request identifier', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ requestId: 'req-1' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await activateMachineRequest(token, 'req/1');
    expect(fetchMock).toHaveBeenCalledWith('/api/admin/machine-requests/req%2F1/provisioning/activate', {
      method: 'POST', headers: { Authorization: 'Bearer access-token' },
    });
  });

  it('marks Ready using only the request identifier', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ requestId: 'req-1', preparationStatus: 'ready' }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await markMachineRequestReady(token, 'req/1');
    expect(fetchMock).toHaveBeenCalledWith('/api/admin/machine-requests/req%2F1/ready', {
      method: 'POST', headers: { Authorization: 'Bearer access-token' },
    });
  });

  it('downloads the authenticated PDF using the backend filename', async () => {
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const createObjectURL = vi.fn().mockReturnValue('blob:test');
    const revokeObjectURL = vi.fn();
    vi.stubGlobal('URL', { ...URL, createObjectURL, revokeObjectURL });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(new Blob(['pdf']), { status: 200, headers: { 'Content-Disposition': "attachment; filename*=UTF-8''manuel%20machine.pdf" } })));

    await expect(downloadMachineRequestDocument(token, 'req-1', 'opaque-id', 'fallback.pdf')).resolves.toEqual({ kind: 'success', data: null });
    expect(click).toHaveBeenCalledOnce();
    expect(createObjectURL).toHaveBeenCalledOnce();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:test');
    click.mockRestore();
  });
});
