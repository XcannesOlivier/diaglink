import { getApiAuthHeaders } from '../utils/apiAuth';
import { parseApiWriteResult } from '../utils/apiResponse';
export interface FinanceSummary {
  machineCreditResetUtc: string | null;
  machineCreditRemaining: number | null; walletBalance: number; subscriptionStatus: string | null;
  nextDueUtc: string | null; cancelAtPeriodEnd: boolean; unpaidInvoiceAmount: number | null;
  rechargeEnabled: boolean; creditStatus: string | null; creditMessage: string | null;
}
export interface ClientTopUp { id: string; amount: number; status: string; paymentUrl: string | null }
export interface CompanyConsumptionUser {
  id: string | null; name: string; includedQuotaConsumed: number; commercialCredit: number;
}
export interface CompanyConsumptionMachine {
  id: string; name: string; billable: boolean; hasPaidRights: boolean;
  includedQuotaBudget: number; resetUtc: string | null; commercialCredit: number;
  users: CompanyConsumptionUser[];
}
export interface CompanyConsumptionReport { machines: CompanyConsumptionMachine[] }
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

export function companyConsumptionRequest(token: () => Promise<string | null>, filter: {from?: string;to: string;usageType?: string}) {
  const query=new URLSearchParams({to:filter.to});
  if(filter.from)query.set('from',filter.from);
  if(filter.usageType)query.set('usageType',filter.usageType);
  return financeRequest<CompanyConsumptionReport>(token,`/consumption?${query}`);
}
