import { getApiAuthHeaders } from '../utils/apiAuth';
import { parseApiResult } from '../utils/apiResponse';
import type { ApiResult } from '../types/apiResult';
import type { UsageFilter, AiUsageSummaryDto, CompanyUsageDto, MachineUsageDto, UserUsageDto } from '../types/aiUsage';

type TokenProvider = () => Promise<string | null>;
async function get<T>(token: TokenProvider, path: string, filter: UsageFilter, signal?: AbortSignal, scope: 'admin' | 'company' = 'admin'): Promise<ApiResult<T>> {
  try {
    const query = new URLSearchParams({ to: filter.to });
    if (filter.from) query.set('from', filter.from);
    if (filter.usageType) query.set('usageType', filter.usageType);
    const { headers, mode } = await getApiAuthHeaders(token);
    const response = await fetch(`${import.meta.env.VITE_API_URL || '/api'}/${scope}/usage/${path}${path.includes('?') ? '&' : '?'}${query}`, { headers, signal });
    return await parseApiResult<T>(response, mode);
  } catch { return { kind: 'error' }; }
}
const scope = (id: string | null) => encodeURIComponent(id ?? 'unassigned');
export const getCompanyUsageSummary = (t: TokenProvider, f: UsageFilter, s?: AbortSignal) =>
  get<AiUsageSummaryDto>(t, 'summary', f, s, 'company');
export const getCompanyUsageMachines = (t: TokenProvider, f: UsageFilter, s?: AbortSignal) =>
  get<MachineUsageDto[]>(t, 'machines', f, s, 'company');
export const getCompanyUsageUsers = (t: TokenProvider, machineId: string | null, f: UsageFilter, s?: AbortSignal) =>
  get<UserUsageDto[]>(t, `machines/${scope(machineId)}/users`, f, s, 'company');
export const getUsageSummary = (t: TokenProvider, f: UsageFilter, s?: AbortSignal) => get<AiUsageSummaryDto>(t, 'summary', f, s);
export const getUsageCompanies = (t: TokenProvider, f: UsageFilter, s?: AbortSignal) => get<CompanyUsageDto[]>(t, 'companies', f, s);
export const getUsageMachines = (t: TokenProvider, companyId: string | null, f: UsageFilter, s?: AbortSignal) =>
  get<MachineUsageDto[]>(t, `companies/${scope(companyId)}/machines`, f, s);
export const getUsageUsers = (t: TokenProvider, companyId: string | null, machineId: string | null, f: UsageFilter, s?: AbortSignal) =>
  get<UserUsageDto[]>(t, `machines/${scope(machineId)}/users?companyId=${scope(companyId)}`, f, s);
