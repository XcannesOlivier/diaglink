import { getApiAuthHeaders } from '../utils/apiAuth';
import { parseApiResult, parseApiWriteResult } from '../utils/apiResponse';
import type { ApiResult, ApiWriteResult } from '../types/apiResult';
import type { CompanyDto, CompanyOnboardingResultDto } from '../types/company';

const getApiUrl = () => import.meta.env.VITE_API_URL || '/api';

/** GET /api/companies — diaglink_super_admin only. */
export async function getCompanies(
  getAccessToken: () => Promise<string | null>
): Promise<ApiResult<CompanyDto[]>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/companies`, { headers });
    return await parseApiResult<CompanyDto[]>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/** GET /api/company — the caller's own company, resolved server-side from the company_id claim. */
export async function getCompany(
  getAccessToken: () => Promise<string | null>
): Promise<ApiResult<CompanyDto>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/company`, { headers });
    return await parseApiResult<CompanyDto>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/**
 * POST /api/companies/onboard — diaglink_super_admin only. Transactional: creates the company and its
 * first company_admin together. The client never supplies Id/Status/Role/timestamps.
 */
export async function onboardCompany(
  getAccessToken: () => Promise<string | null>,
  companyName: string,
  adminEmail: string,
  firstName: string,
  lastName: string,
  phoneNumber: string
): Promise<ApiWriteResult<CompanyOnboardingResultDto>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/companies/onboard`, {
      method: 'POST',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify({ companyName, adminEmail, firstName, lastName, phoneNumber }),
    });
    return await parseApiWriteResult<CompanyOnboardingResultDto>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}
