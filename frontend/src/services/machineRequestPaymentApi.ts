export type MachineRequestPaymentResponse = {
  paymentRequestId: string;
  status: string;
  amount: number;
  currency: string;
  checkoutUrl?: string;
};

export class MachineRequestPaymentTerminalError extends Error {}

const apiBase = import.meta.env.VITE_API_URL || '/api';

async function readResponse(response: Response): Promise<MachineRequestPaymentResponse> {
  if (!response.ok) throw new Error('Le paiement n’a pas pu être préparé ou vérifié.');
  return await response.json() as MachineRequestPaymentResponse;
}

export async function createMachineRequestPayment(totalPages: number, email: string, idempotencyKey: string) {
  const response = await fetch(`${apiBase}/public/machine-request-payments`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify({ totalPages, email }),
  });
  const payment = await readResponse(response);
  if (!payment.checkoutUrl) throw new Error('La page de paiement Stripe est indisponible.');
  return payment;
}

export async function getMachineRequestPayment(paymentRequestId: string) {
  return await readResponse(await fetch(`${apiBase}/public/machine-request-payments/${paymentRequestId}`));
}

export async function waitForMachineRequestAuthorization(
  paymentRequestId: string,
  pollIntervalMs = 1500,
  maxAttempts = 400,
) {
  for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
    const payment = await getMachineRequestPayment(paymentRequestId);
    if (payment.status === 'authorized') return payment;
    if (payment.status === 'captured' || payment.status === 'cancelled')
      throw new MachineRequestPaymentTerminalError('Cette autorisation de paiement ne peut plus être utilisée.');
    await new Promise(resolve => window.setTimeout(resolve, pollIntervalMs));
  }
  throw new Error('L’autorisation du paiement prend trop de temps. Vous pouvez réessayer sans sélectionner à nouveau vos documents.');
}
