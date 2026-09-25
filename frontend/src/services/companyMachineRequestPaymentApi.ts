import { getApiAuthHeaders } from '../utils/apiAuth';

export type CompanyMachineRequestPayment = {
  paymentRequestId: string;
  status: string;
  amount: number;
  currency: string;
  checkoutUrl?: string;
};
export type AdditionalMachineValues = { machineName: string; manufacturer: string; model: string; serialNumber: string; description: string };
export type CompanyMachineRequestCreated = { requestId: string; status: string; createdAt: string; documentCount: number; totalPages: number; additionalPages: number; preparationTotal: number };

const apiBase = import.meta.env.VITE_API_URL || '/api';

async function readPayment(response: Response): Promise<CompanyMachineRequestPayment> {
  if (!response.ok) {
    let message = 'Le paiement n’a pas pu être préparé ou vérifié.';
    try { message = ((await response.json()) as { error?: string }).error ?? message; } catch { /* public fallback */ }
    throw new Error(message);
  }
  return await response.json() as CompanyMachineRequestPayment;
}

export async function createCompanyMachineRequestPayment(
  getAccessToken: () => Promise<string | null>, documents: File[], idempotencyKey: string,
) {
  const form = new FormData();
  documents.forEach(document => form.append('documents', document));
  const { headers } = await getApiAuthHeaders(getAccessToken);
  return await readPayment(await fetch(`${apiBase}/company/machine-request-payments`, {
    method: 'POST', headers: { ...headers, 'Idempotency-Key': idempotencyKey }, body: form,
  }));
}

export async function getCompanyMachineRequestPayment(
  getAccessToken: () => Promise<string | null>, paymentRequestId: string,
) {
  const { headers } = await getApiAuthHeaders(getAccessToken);
  return await readPayment(await fetch(`${apiBase}/company/machine-request-payments/${encodeURIComponent(paymentRequestId)}`, { headers }));
}

export async function waitForCompanyMachineRequestAuthorization(
  getAccessToken: () => Promise<string | null>, paymentRequestId: string, pollIntervalMs = 1500, maxAttempts = 400,
) {
  for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
    const payment = await getCompanyMachineRequestPayment(getAccessToken, paymentRequestId);
    if (payment.status === 'authorized') return payment;
    if (payment.status === 'captured' || payment.status === 'cancelled') throw new Error('Cette autorisation de paiement ne peut plus être utilisée.');
    await new Promise(resolve => window.setTimeout(resolve, pollIntervalMs));
  }
  throw new Error('L’autorisation du paiement prend trop de temps. Vous pouvez réessayer sans sélectionner à nouveau vos documents.');
}

export async function submitCompanyMachineRequest(getAccessToken: () => Promise<string | null>, paymentRequestId: string,
  machine: AdditionalMachineValues, documents: File[]) {
  const form = new FormData();
  form.append('paymentRequestId', paymentRequestId);
  Object.entries(machine).forEach(([name, value]) => form.append(name, value));
  documents.forEach(document => form.append('documents', document));
  const { headers } = await getApiAuthHeaders(getAccessToken);
  const response = await fetch(`${apiBase}/company/machine-requests`, { method: 'POST', headers, body: form });
  if (!response.ok) {
    let message = 'L’envoi de la demande a échoué.';
    try {
      const body = await response.json() as { error?: string; errors?: Record<string, string[]> };
      message = body.error ?? Object.values(body.errors ?? {}).flat()[0] ?? message;
    } catch { /* public fallback */ }
    throw new Error(message);
  }
  return await response.json() as CompanyMachineRequestCreated;
}
