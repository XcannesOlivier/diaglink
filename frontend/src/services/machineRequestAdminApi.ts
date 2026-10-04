import type { ApiResult, ApiWriteResult } from '../types/apiResult';
import { getApiAuthHeaders } from '../utils/apiAuth';
import { parseApiResult, parseApiWriteResult } from '../utils/apiResponse';

const getApiUrl = () => import.meta.env.VITE_API_URL || '/api';

export type MachineRequestStatus = 'pending' | 'treated' | 'rejected';

export interface MachineRequestListItem {
  requestId: string;
  createdAt: string;
  status: MachineRequestStatus;
  firstName: string;
  lastName: string;
  company: string;
  email: string;
  phone: string;
  machineName: string;
  manufacturer: string;
  model: string;
  serialNumber?: string | null;
  description?: string | null;
  documentCount: number;
  totalPages: number;
  preparationTotal: number;
  requestKind?: 'initialMachine' | 'additionalMachine' | 'additionalDocuments';
  companyId?: string | null;
  requestedByUserId?: string | null;
  isArchived?: boolean;
  archivedAtUtc?: string | null;
  archivedByUserId?: string | null;
}

export interface MachineRequestDetail {
  requestId: string;
  createdAt: string;
  status: MachineRequestStatus;
  client: { firstName: string; lastName: string; company: string; email: string; phone: string };
  machine: { machineName: string; manufacturer: string; model: string; serialNumber: string | null; description: string | null };
  documents: Array<{ documentId: string; originalName: string; size: number; pageCount: number }>;
  pricing: {
    totalPages: number;
    includedPages: number;
    additionalPages: number;
    basePreparationPrice: number;
    additionalPagePrice: number;
    preparationTotal: number;
    monthlySubscriptionPrice: number;
  };
  requestKind?: 'initialMachine' | 'additionalMachine' | 'additionalDocuments';
  companyId?: string | null;
  requestedByUserId?: string | null;
  preparationStatus?: 'pending' | 'ready';
  readyAtUtc?: string | null;
  readyByUserId?: string | null;
  isArchived?: boolean;
  archivedAtUtc?: string | null;
  archivedByUserId?: string | null;
  payment: {
    paymentRequestId: string;
    status: 'pending' | 'authorized' | 'captured' | 'cancelled' | 'abandoned' | 'unknown';
    amount: number;
    currency: string;
    authorizationInsufficient: boolean;
    initialAuthorizationAmount?: number | null;
    provisioningStage?: 'awaitingAcceptance' | 'amountFinalized' | 'businessEntitiesCreated' | 'customerLinked' | 'subscriptionCreated' | 'initialPeriodCreated' | 'completed';
    companyId?: string | null;
    machineId?: string | null;
    preparationAmount?: number;
    activatedAtUtc?: string | null;
    firstPeriodEndUtc?: string | null;
    serviceAmountCents?: number | null;
  } | null;
}

export async function markMachineRequestReady(
  getAccessToken: () => Promise<string | null>, requestId: string,
): Promise<ApiWriteResult<MachineRequestDetail>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    return await parseApiWriteResult<MachineRequestDetail>(await fetch(
      `${getApiUrl()}/admin/machine-requests/${encodeURIComponent(requestId)}/ready`,
      { method: 'POST', headers },
    ), mode);
  } catch { return { kind: 'error' }; }
}

export interface MachineRequestPaymentAction {
  paymentRequestId: string;
  status: string;
  amount: number;
  currency: string;
  initialAuthorizationAmount?: number | null;
}

export async function listMachineRequests(getAccessToken: () => Promise<string | null>): Promise<ApiResult<MachineRequestListItem[]>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    return await parseApiResult<MachineRequestListItem[]>(await fetch(`${getApiUrl()}/admin/machine-requests`, { headers }), mode);
  } catch {
    return { kind: 'error' };
  }
}

export async function listArchivedMachineRequests(getAccessToken: () => Promise<string | null>): Promise<ApiResult<MachineRequestListItem[]>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    return await parseApiResult<MachineRequestListItem[]>(await fetch(`${getApiUrl()}/admin/machine-requests/history`, { headers }), mode);
  } catch {
    return { kind: 'error' };
  }
}

export async function updateMachineRequestArchive(
  getAccessToken: () => Promise<string | null>,
  requestId: string,
  isArchived: boolean,
): Promise<ApiWriteResult<MachineRequestDetail>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    return await parseApiWriteResult<MachineRequestDetail>(await fetch(`${getApiUrl()}/admin/machine-requests/${encodeURIComponent(requestId)}/archive`, {
      method: 'PATCH',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify({ isArchived }),
    }), mode);
  } catch {
    return { kind: 'error' };
  }
}

export async function getMachineRequest(getAccessToken: () => Promise<string | null>, requestId: string): Promise<ApiResult<MachineRequestDetail>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    return await parseApiResult<MachineRequestDetail>(await fetch(`${getApiUrl()}/admin/machine-requests/${encodeURIComponent(requestId)}`, { headers }), mode);
  } catch {
    return { kind: 'error' };
  }
}

export async function updateMachineRequestStatus(
  getAccessToken: () => Promise<string | null>,
  requestId: string,
  status: MachineRequestStatus,
): Promise<ApiWriteResult<MachineRequestDetail>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    return await parseApiWriteResult<MachineRequestDetail>(await fetch(`${getApiUrl()}/admin/machine-requests/${encodeURIComponent(requestId)}/status`, {
      method: 'PATCH',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify({ status }),
    }), mode);
  } catch {
    return { kind: 'error' };
  }
}

async function changeMachineRequestPayment(
  getAccessToken: () => Promise<string | null>,
  paymentRequestId: string,
  action: 'capture' | 'cancel',
): Promise<ApiWriteResult<MachineRequestPaymentAction>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    return await parseApiWriteResult<MachineRequestPaymentAction>(await fetch(
      `${getApiUrl()}/admin/machine-request-payments/${encodeURIComponent(paymentRequestId)}/${action}`,
      { method: 'POST', headers },
    ), mode);
  } catch {
    return { kind: 'error' };
  }
}

export const captureMachineRequestPayment = (getAccessToken: () => Promise<string | null>, paymentRequestId: string) =>
  changeMachineRequestPayment(getAccessToken, paymentRequestId, 'capture');

export const cancelMachineRequestPayment = (getAccessToken: () => Promise<string | null>, paymentRequestId: string) =>
  changeMachineRequestPayment(getAccessToken, paymentRequestId, 'cancel');

export async function decideAdditionalMachineRequest(getAccessToken: () => Promise<string | null>, requestId: string,
  decision: 'accept' | 'reject'): Promise<ApiWriteResult<MachineRequestDetail>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    return await parseApiWriteResult<MachineRequestDetail>(await fetch(
      `${getApiUrl()}/admin/machine-requests/${encodeURIComponent(requestId)}/additional-machine/${decision}`,
      { method: 'POST', headers },
    ), mode);
  } catch { return { kind: 'error' }; }
}

export async function decideAdditionalDocumentsRequest(getAccessToken: () => Promise<string | null>, requestId: string,
  decision: 'accept' | 'reject'): Promise<ApiWriteResult<MachineRequestDetail>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    return await parseApiWriteResult<MachineRequestDetail>(await fetch(
      `${getApiUrl()}/admin/machine-requests/${encodeURIComponent(requestId)}/additional-documents/${decision}`,
      { method: 'POST', headers },
    ), mode);
  } catch { return { kind: 'error' }; }
}

export async function attachMachineRequestBusinessEntities(
  getAccessToken: () => Promise<string | null>,
  requestId: string,
  companyId: string,
  machineId: string,
): Promise<ApiWriteResult<MachineRequestDetail>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    return await parseApiWriteResult<MachineRequestDetail>(await fetch(
      `${getApiUrl()}/admin/machine-requests/${encodeURIComponent(requestId)}/provisioning/business-entities`,
      {
        method: 'PATCH',
        headers: { ...headers, 'Content-Type': 'application/json' },
        body: JSON.stringify({ companyId, machineId }),
      },
    ), mode);
  } catch {
    return { kind: 'error' };
  }
}

export async function linkMachineRequestCustomer(
  getAccessToken: () => Promise<string | null>,
  requestId: string,
): Promise<ApiWriteResult<MachineRequestDetail>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    return await parseApiWriteResult<MachineRequestDetail>(await fetch(
      `${getApiUrl()}/admin/machine-requests/${encodeURIComponent(requestId)}/provisioning/customer`,
      { method: 'POST', headers },
    ), mode);
  } catch {
    return { kind: 'error' };
  }
}

export async function configureMachineRequestSubscription(
  getAccessToken: () => Promise<string | null>, requestId: string,
): Promise<ApiWriteResult<MachineRequestDetail>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    return await parseApiWriteResult<MachineRequestDetail>(await fetch(
      `${getApiUrl()}/admin/machine-requests/${encodeURIComponent(requestId)}/provisioning/subscription`,
      { method: 'POST', headers },
    ), mode);
  } catch { return { kind: 'error' }; }
}

export async function activateMachineRequest(
  getAccessToken: () => Promise<string | null>, requestId: string,
): Promise<ApiWriteResult<MachineRequestDetail>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    return await parseApiWriteResult<MachineRequestDetail>(await fetch(
      `${getApiUrl()}/admin/machine-requests/${encodeURIComponent(requestId)}/provisioning/activate`,
      { method: 'POST', headers },
    ), mode);
  } catch { return { kind: 'error' }; }
}

export async function downloadMachineRequestDocument(
  getAccessToken: () => Promise<string | null>,
  requestId: string,
  documentId: string,
  fallbackName: string,
): Promise<ApiResult<null>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/admin/machine-requests/${encodeURIComponent(requestId)}/documents/${encodeURIComponent(documentId)}`, { headers });
    if (response.status === 401) return { kind: 'unauthorized', diagLinkSessionExpired: mode === 'diaglink' };
    if (response.status === 403) return { kind: 'forbidden' };
    if (response.status === 404) return { kind: 'not-found' };
    if (!response.ok) return { kind: 'error' };

    const disposition = response.headers.get('Content-Disposition') ?? '';
    const encodedName = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
    const quotedName = disposition.match(/filename="([^"]+)"/i)?.[1];
    const fileName = encodedName ? decodeURIComponent(encodedName) : quotedName || fallbackName;
    const url = URL.createObjectURL(await response.blob());
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
    return { kind: 'success', data: null };
  } catch {
    return { kind: 'error' };
  }
}
