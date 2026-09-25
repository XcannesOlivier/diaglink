import { getApiAuthHeaders } from '../utils/apiAuth';

const apiBase = import.meta.env.VITE_API_URL || '/api';

export type AdditionalDocumentsStage = {
  requestId: string;
  paymentRequestId: string;
  status: 'pending';
  targetMachineId: string;
  documentCount: number;
  totalPages: number;
  amountCents: number;
  currency: 'EUR';
};

export type AdditionalDocumentsPayment = {
  paymentRequestId: string;
  status: 'pending' | 'authorized' | 'captured' | 'cancelled' | 'abandoned';
  amount: number;
  currency: 'EUR';
  checkoutUrl?: string;
};

async function errorMessage(response: Response, fallback: string) {
  try {
    const body = await response.json() as { error?: string; errors?: Record<string, string[]> };
    return body.error ?? Object.values(body.errors ?? {}).flat()[0] ?? fallback;
  } catch { return fallback; }
}

export async function stageAdditionalDocuments(getAccessToken: () => Promise<string | null>, machineId: string,
  documents: File[], idempotencyKey: string): Promise<AdditionalDocumentsStage> {
  const form = new FormData();
  documents.forEach(document => form.append('documents', document));
  const { headers } = await getApiAuthHeaders(getAccessToken);
  const response = await fetch(`${apiBase}/company/machines/${encodeURIComponent(machineId)}/document-requests`, {
    method: 'POST', headers: { ...headers, 'Idempotency-Key': idempotencyKey }, body: form,
  });
  if (!response.ok) throw new Error(await errorMessage(response, 'Les documents n’ont pas pu être reçus.'));
  return await response.json() as AdditionalDocumentsStage;
}

async function readPayment(response: Response): Promise<AdditionalDocumentsPayment> {
  if (!response.ok) throw new Error(await errorMessage(response, 'L’autorisation n’a pas pu être vérifiée.'));
  return await response.json() as AdditionalDocumentsPayment;
}

export async function startAdditionalDocumentsPayment(getAccessToken: () => Promise<string | null>, requestId: string) {
  const { headers } = await getApiAuthHeaders(getAccessToken);
  return await readPayment(await fetch(`${apiBase}/company/document-requests/${encodeURIComponent(requestId)}/payment`, {
    method: 'POST', headers,
  }));
}

export async function getAdditionalDocumentsPayment(getAccessToken: () => Promise<string | null>, requestId: string) {
  const { headers } = await getApiAuthHeaders(getAccessToken);
  return await readPayment(await fetch(`${apiBase}/company/document-requests/${encodeURIComponent(requestId)}/payment`, { headers }));
}

export async function cancelAdditionalDocumentsRequest(getAccessToken: () => Promise<string | null>, requestId: string) {
  const { headers } = await getApiAuthHeaders(getAccessToken);
  const response = await fetch(`${apiBase}/company/document-requests/${encodeURIComponent(requestId)}/cancel`, {
    method: 'POST', headers,
  });
  if (!response.ok) throw new Error(await errorMessage(response, 'La demande n’a pas pu être annulée.'));
  return await response.json() as { paymentRequestId: string; paymentStatus: 'cancelled' | 'abandoned'; requestStatus: 'rejected' };
}
