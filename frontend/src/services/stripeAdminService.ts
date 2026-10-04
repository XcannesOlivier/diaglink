import { getApiAuthHeaders } from '../utils/apiAuth';
import { parseApiWriteResult } from '../utils/apiResponse';
import type { ApiWriteResult } from '../types/apiResult';

export interface SubscriptionPaymentOverview {
  invoice: { id: string; status: string; billingReason: string; startUtc: string; endUtc: string;
    quantity: number; amountPaidCents: number; paymentUrl: string | null } | null;
  payments: { stripeInvoiceId: string; billingReason: string; status: string; periodStartUtc: string; periodEndUtc: string }[];
}
export async function stripeSubscriptionPaymentRequest(token: () => Promise<string | null>, companyId: string): Promise<ApiWriteResult<SubscriptionPaymentOverview>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(token);
    const base = import.meta.env.VITE_API_URL || '/api';
    return await parseApiWriteResult<SubscriptionPaymentOverview>(await fetch(`${base}/companies/${encodeURIComponent(companyId)}/stripe/subscription-payment`, { headers }), mode);
  } catch { return { kind: 'error' }; }
}

export interface StripeCompanySummary {
  machines?: {id: string; name: string; billable: boolean; rightsEndUtc: string | null}[];
  cancelAtPeriodEnd?: boolean;
  latestInvoiceId?: string | null;
  latestInvoiceStatus?: string | null;
  amountRemainingCents?: number | null;
  billingAccountId: string | null;
  stripeCustomerId: string | null;
  stripeSubscriptionId: string | null;
  subscriptionStatus: string | null;
  currentPeriodStartUtc: string | null;
  currentPeriodEndUtc: string | null;
  activeMachineCount: number;
  machineRequestProvisioningCompleted: boolean;
  testActionsEnabled: boolean;
  activeMachines: { id: string; name: string; hasBillingPeriod: boolean }[];
}

export async function setStripeMachineStatus(token: () => Promise<string | null>, companyId: string, machineId: string,
  active: boolean, requestId: string): Promise<ApiWriteResult<{status: string}>> {
  try {
    const {headers,mode}=await getApiAuthHeaders(token);
    return await parseApiWriteResult(await fetch(`${import.meta.env.VITE_API_URL || '/api'}/companies/${encodeURIComponent(companyId)}/stripe/machines/${encodeURIComponent(machineId)}/status`,
      {method:'POST',headers:{...headers,'Content-Type':'application/json'},body:JSON.stringify({active,requestId})}),mode);
  } catch {return {kind:'error'};}
}

export async function setCompanyStripeMachineStatus(token: () => Promise<string | null>, machineId: string,
  active: boolean, requestId: string): Promise<ApiWriteResult<{status: string}>> {
  try {
    const {headers,mode}=await getApiAuthHeaders(token);
    return await parseApiWriteResult(await fetch(`${import.meta.env.VITE_API_URL || '/api'}/company/stripe/machines/${encodeURIComponent(machineId)}/status`,
      {method:'POST',headers:{...headers,'Content-Type':'application/json'},body:JSON.stringify({active,requestId})}),mode);
  } catch {return {kind:'error'};}
}

export interface StripeAdditionSummary {
  id: string; machineId: string; machineName: string; stage: string;
  stripeInvoiceId: string | null; targetQuantity: number; aiAmountEur: number; serviceAmountEur: number;
  activatedAtUtc: string; cycleStartUtc: string; cycleEndUtc: string;
  paymentConfirmedAtUtc: string | null; machineBillingPeriodId: string | null;
  externalEventId: string | null; completedAtUtc: string | null; reconciliationRequired: boolean;
}
export interface StripeAdditionResult { operationId: string; status: string }

async function additionsRequest<T>(getAccessToken: () => Promise<string | null>, companyId: string,
  machineId?: string): Promise<ApiWriteResult<T>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const base = import.meta.env.VITE_API_URL || '/api';
    const response = await fetch(`${base}/companies/${encodeURIComponent(companyId)}/stripe/machine-additions${machineId ? `/${encodeURIComponent(machineId)}` : ''}`,
      { headers, method: machineId ? 'POST' : 'GET' });
    return await parseApiWriteResult<T>(response, mode);
  } catch { return { kind: 'error' }; }
}

export const getStripeAdditions = (token: () => Promise<string | null>, companyId: string) =>
  additionsRequest<StripeAdditionSummary[]>(token, companyId);
export const addStripeMachine = (token: () => Promise<string | null>, companyId: string, machineId: string) =>
  additionsRequest<StripeAdditionResult>(token, companyId, machineId);

export async function stripeCompanyRequest(getAccessToken: () => Promise<string | null>, companyId: string,
  action?: 'customer' | 'subscription'): Promise<ApiWriteResult<StripeCompanySummary>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const base = import.meta.env.VITE_API_URL || '/api';
    const response = await fetch(`${base}/companies/${encodeURIComponent(companyId)}/stripe${action ? `/${action}` : ''}`,
      { headers, method: action ? 'POST' : 'GET' });
    return await parseApiWriteResult<StripeCompanySummary>(response, mode);
  } catch { return { kind: 'error' }; }
}
