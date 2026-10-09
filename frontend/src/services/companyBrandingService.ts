import { getApiAuthHeaders } from '../utils/apiAuth';
import { parseApiWriteResult } from '../utils/apiResponse';
import type { ApiWriteResult } from '../types/apiResult';

const apiUrl = import.meta.env.VITE_API_URL || '/api';
export const MAX_COMPANY_LOGO_BYTES = 2 * 1024 * 1024;

interface CompanyBrandingResponse {
  accentColor: string | null;
  hasLogo: boolean;
}

async function writeBranding(
  getAccessToken: () => Promise<string | null>,
  method: 'PATCH' | 'PUT' | 'DELETE',
  path = '',
  body?: FormData | string,
): Promise<ApiWriteResult<CompanyBrandingResponse | { success: boolean }>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${apiUrl}/company/branding${path}`, {
      method,
      headers: typeof body === 'string' ? { ...headers, 'Content-Type': 'application/json' } : headers,
      ...(body !== undefined ? { body } : {}),
    });
    return await parseApiWriteResult<CompanyBrandingResponse | { success: boolean }>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

export function updateAccentColor(getAccessToken: () => Promise<string | null>, accentColor: string | null) {
  if (accentColor !== null && !/^#[0-9a-f]{6}$/i.test(accentColor)) {
    return Promise.resolve<ApiWriteResult<CompanyBrandingResponse | { success: boolean }>>({
      kind: 'validation-error', message: 'Choisissez une couleur au format #RRGGBB.',
    });
  }
  return writeBranding(getAccessToken, 'PATCH', '', JSON.stringify({ accentColor }));
}

export function validateCompanyLogo(file: File): string | null {
  if (!['image/png', 'image/jpeg'].includes(file.type)) return 'Choisissez un fichier PNG ou JPEG/JPG.';
  if (file.size === 0 || file.size > MAX_COMPANY_LOGO_BYTES) return 'Le logo doit être non vide et ne pas dépasser 2 Mo.';
  return null;
}

export function uploadLogo(getAccessToken: () => Promise<string | null>, file: File) {
  const error = validateCompanyLogo(file);
  if (error) {
    return Promise.resolve<ApiWriteResult<CompanyBrandingResponse | { success: boolean }>>({
      kind: 'validation-error', message: error,
    });
  }
  const form = new FormData();
  form.append('logo', file);
  return writeBranding(getAccessToken, 'PUT', '/logo', form);
}

export function deleteLogo(getAccessToken: () => Promise<string | null>) {
  return writeBranding(getAccessToken, 'DELETE', '/logo');
}

export function resetBranding(getAccessToken: () => Promise<string | null>) {
  return writeBranding(getAccessToken, 'DELETE');
}

export interface FetchCompanyLogoResult {
  logo: Blob | null;
  diagLinkSessionExpired: boolean;
}

/** Fetches the current tenant logo through the authenticated backend proxy. */
export async function fetchCompanyLogo(
  getAccessToken: () => Promise<string | null>,
  signal?: AbortSignal,
): Promise<FetchCompanyLogoResult> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const apiUrl = import.meta.env.VITE_API_URL || '/api';
    const response = await fetch(`${apiUrl}/company/branding/logo`, { headers, signal });

    if (response.status === 404) {
      return { logo: null, diagLinkSessionExpired: false };
    }
    if (!response.ok) {
      return {
        logo: null,
        diagLinkSessionExpired: response.status === 401 && mode === 'diaglink',
      };
    }

    return { logo: await response.blob(), diagLinkSessionExpired: false };
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      return { logo: null, diagLinkSessionExpired: false };
    }
    return { logo: null, diagLinkSessionExpired: false };
  }
}
