import { getApiAuthHeaders } from '../utils/apiAuth';
import { parseApiWriteResult } from '../utils/apiResponse';
import type { ApiWriteResult } from '../types/apiResult';
export interface WalletTopUp {
  id: string; stage: string; status: string; amountEur: number; paymentUrl: string | null;
  stripeSessionId: string | null; stripePaymentIntentId: string | null; ledgerEntryId: string | null;
  externalEventId: string | null; createdAtUtc: string; paymentConfirmedAtUtc: string | null; completedAtUtc: string | null;
}
export interface WalletOverview { balance: number; currency: string; walletExists: boolean; enabled: boolean; operations: WalletTopUp[] }
async function request<T>(token: () => Promise<string | null>, company: string, body?: { requestId: string; amount: number; currency: 'EUR' }): Promise<ApiWriteResult<T>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(token);
    const response = await fetch(`${import.meta.env.VITE_API_URL || '/api'}/companies/${encodeURIComponent(company)}/stripe/wallet-topups`,
      { method: body ? 'POST' : 'GET', headers: body ? { ...headers, 'Content-Type': 'application/json' } : headers,
        ...(body ? { body: JSON.stringify(body) } : {}) });
    return await parseApiWriteResult<T>(response, mode);
  } catch { return { kind: 'error' }; }
}
export const getWalletTopUps = (token: () => Promise<string | null>, company: string) => request<WalletOverview>(token, company);
export const startWalletTopUp = (token: () => Promise<string | null>, company: string, requestId: string, amount: number) =>
  request<WalletTopUp>(token, company, { requestId, amount, currency: 'EUR' });
