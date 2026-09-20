import { getApiAuthHeaders } from '../utils/apiAuth';
import { parseApiResult, parseApiWriteResult } from '../utils/apiResponse';
import type { ApiResult, ApiWriteResult } from '../types/apiResult';
import type { CompanyUserDto } from '../types/company';
import type { UserMachineAccessDto } from '../types/machine';

const getApiUrl = () => import.meta.env.VITE_API_URL || '/api';

/** Body for POST /api/company/users and POST /api/companies/{companyId}/users — role is restricted
 * server-side to technician/company_admin; diaglink_super_admin can never be sent here. */
export interface CreateCompanyUserRequest {
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber: string;
  role: string;
}

/**
 * GET /api/company/users — always scoped server-side to the caller's own company_id claim.
 * Never call this for diaglink_super_admin expecting a cross-company listing (see UsersView).
 */
export async function getCompanyUsers(
  getAccessToken: () => Promise<string | null>
): Promise<ApiResult<CompanyUserDto[]>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/company/users`, { headers });
    return await parseApiResult<CompanyUserDto[]>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/** GET /api/companies/{companyId}/users — diaglink_super_admin only. */
export async function getUsersByCompany(
  getAccessToken: () => Promise<string | null>,
  companyId: string
): Promise<ApiResult<CompanyUserDto[]>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/companies/${encodeURIComponent(companyId)}/users`, { headers });
    return await parseApiResult<CompanyUserDto[]>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/** POST /api/companies/{companyId}/users — diaglink_super_admin only. */
export async function createUserForCompany(
  getAccessToken: () => Promise<string | null>,
  companyId: string,
  request: CreateCompanyUserRequest
): Promise<ApiWriteResult<CompanyUserDto>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/companies/${encodeURIComponent(companyId)}/users`, {
      method: 'POST',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    return await parseApiWriteResult<CompanyUserDto>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/**
 * POST /api/company/users — company_admin only. Creates a user (technician or company_admin) in the
 * caller's OWN company; CompanyId/Status are always forced server-side from the claim, never sent
 * by the client.
 */
export async function createTechnician(
  getAccessToken: () => Promise<string | null>,
  request: CreateCompanyUserRequest
): Promise<ApiWriteResult<CompanyUserDto>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/company/users`, {
      method: 'POST',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    return await parseApiWriteResult<CompanyUserDto>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/** GET /api/company/users/{userId}/machines — every company machine, flagged with the technician's current access. */
export async function getUserMachineAccess(
  getAccessToken: () => Promise<string | null>,
  userId: string
): Promise<ApiResult<UserMachineAccessDto[]>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/company/users/${encodeURIComponent(userId)}/machines`, { headers });
    return await parseApiResult<UserMachineAccessDto[]>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/** GET /api/companies/{companyId}/users/{userId}/machines — diaglink_super_admin only. */
export async function getUserMachineAccessForCompany(
  getAccessToken: () => Promise<string | null>,
  companyId: string,
  userId: string
): Promise<ApiResult<UserMachineAccessDto[]>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/companies/${encodeURIComponent(companyId)}/users/${encodeURIComponent(userId)}/machines`, { headers });
    return await parseApiResult<UserMachineAccessDto[]>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/**
 * PUT /api/company/users/{userId}/machines — replaces the technician's ENTIRE machine access set in
 * one transaction. Used by the "Enregistrer les accès" button rather than one call per checkbox toggle.
 */
export async function replaceUserMachineAccess(
  getAccessToken: () => Promise<string | null>,
  userId: string,
  machineIds: string[]
): Promise<ApiWriteResult<null>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/company/users/${encodeURIComponent(userId)}/machines`, {
      method: 'PUT',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify({ machineIds }),
    });
    return await parseApiWriteResult<null>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/** PUT /api/companies/{companyId}/users/{userId}/machines — diaglink_super_admin only. */
export async function replaceUserMachineAccessForCompany(
  getAccessToken: () => Promise<string | null>,
  companyId: string,
  userId: string,
  machineIds: string[]
): Promise<ApiWriteResult<null>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/companies/${encodeURIComponent(companyId)}/users/${encodeURIComponent(userId)}/machines`, {
      method: 'PUT',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify({ machineIds }),
    });
    return await parseApiWriteResult<null>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/**
 * DELETE /api/company/users/{userId} — company_admin only. Soft-deletes (backend sets Status to
 * inactive) a user in the caller's OWN company; CompanyId is always resolved server-side.
 */
export async function deleteCompanyUser(
  getAccessToken: () => Promise<string | null>,
  userId: string
): Promise<ApiWriteResult<null>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/company/users/${encodeURIComponent(userId)}`, {
      method: 'DELETE',
      headers,
    });
    return await parseApiWriteResult<null>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/** DELETE /api/companies/{companyId}/users/{userId} — diaglink_super_admin only. */
export async function deleteUserForCompany(
  getAccessToken: () => Promise<string | null>,
  companyId: string,
  userId: string
): Promise<ApiWriteResult<null>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/companies/${encodeURIComponent(companyId)}/users/${encodeURIComponent(userId)}`, {
      method: 'DELETE',
      headers,
    });
    return await parseApiWriteResult<null>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/** POST /api/company/users/{userId}/reactivate — company_admin only. Reverses deleteCompanyUser. */
export async function reactivateCompanyUser(
  getAccessToken: () => Promise<string | null>,
  userId: string
): Promise<ApiWriteResult<null>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/company/users/${encodeURIComponent(userId)}/reactivate`, {
      method: 'POST',
      headers,
    });
    return await parseApiWriteResult<null>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/** POST /api/companies/{companyId}/users/{userId}/reactivate — diaglink_super_admin only. */
export async function reactivateUserForCompany(
  getAccessToken: () => Promise<string | null>,
  companyId: string,
  userId: string
): Promise<ApiWriteResult<null>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/companies/${encodeURIComponent(companyId)}/users/${encodeURIComponent(userId)}/reactivate`, {
      method: 'POST',
      headers,
    });
    return await parseApiWriteResult<null>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/**
 * DELETE /api/company/users/{userId}/permanent — company_admin only. Irreversible: removes the
 * dbo.Users row itself, distinct from deleteCompanyUser's soft Status="inactive".
 */
export async function permanentlyDeleteCompanyUser(
  getAccessToken: () => Promise<string | null>,
  userId: string
): Promise<ApiWriteResult<null>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/company/users/${encodeURIComponent(userId)}/permanent`, {
      method: 'DELETE',
      headers,
    });
    return await parseApiWriteResult<null>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/** DELETE /api/companies/{companyId}/users/{userId}/permanent — diaglink_super_admin only. */
export async function permanentlyDeleteUserForCompany(
  getAccessToken: () => Promise<string | null>,
  companyId: string,
  userId: string
): Promise<ApiWriteResult<null>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/companies/${encodeURIComponent(companyId)}/users/${encodeURIComponent(userId)}/permanent`, {
      method: 'DELETE',
      headers,
    });
    return await parseApiWriteResult<null>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}


