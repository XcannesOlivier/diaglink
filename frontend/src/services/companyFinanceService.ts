import { getApiAuthHeaders } from '../utils/apiAuth';
import { parseApiWriteResult } from '../utils/apiResponse';
export interface FinanceSummary {
  machineCreditResetUtc: string | null;
  machineCreditRemaining: number | null; walletBalance: number; subscriptionStatus: string | null;
  nextDueUtc: string | null; cancelAtPeriodEnd: boolean; unpaidInvoiceAmount: number | null;
  rechargeEnabled: boolean; creditStatus: string | null; creditMessage: string | null;
}
export interface ClientTopUp { id: string; amount: number; status: string; paymentUrl: string | null }
export async function financeRequest<T>(token: () => Promise<string | null>, path: string, body?: unknown) {
  try {
    const {headers,mode}=await getApiAuthHeaders(token);
    const response=await fetch(`${import.meta.env.VITE_API_URL || '/api'}/company/finance${path}`,{
      method: body ? 'POST':'GET',headers: body ? {...headers,'Content-Type':'application/json'}:headers,
      ...(body ? {body:JSON.stringify(body)}:{})
    });
    return await parseApiWriteResult<T>(response,mode);
  } catch { return {kind:'error'} as const; }
}
