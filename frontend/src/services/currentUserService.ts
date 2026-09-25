import { getApiAuthHeaders } from '../utils/apiAuth';
import type { CurrentUser } from '../types/currentUser';

export interface FetchCurrentUserResult {
  currentUser: CurrentUser | null;
  /** True when the failure came from an expired/invalid DiagLink session — caller should log out. */
  diagLinkSessionExpired: boolean;
}

/**
 * Calls GET /api/auth/me using the same dynamic auth headers as the rest of the app (DiagLink
 * session or Microsoft bearer, see apiAuth.getApiAuthHeaders). Role/companyId always come from
 * this response — never invented client-side.
 */
export async function fetchCurrentUser(
  getAccessToken: () => Promise<string | null>
): Promise<FetchCurrentUserResult> {
  const { headers, mode } = await getApiAuthHeaders(getAccessToken);
  const apiUrl = import.meta.env.VITE_API_URL || '/api';

  const response = await fetch(`${apiUrl}/auth/me`, { headers });

  if (!response.ok) {
    return {
      currentUser: null,
      diagLinkSessionExpired: response.status === 401 && mode === 'diaglink',
    };
  }

  const data = await response.json();
  return {
    currentUser: {
      userId: data.userId,
      companyId: data.companyId,
      role: data.role,
      email: data.email,
      firstName: data.firstName,
      lastName: data.lastName,
    },
    diagLinkSessionExpired: false,
  };
}
