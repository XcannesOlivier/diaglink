const DIAGLINK_TOKEN_KEY = 'diaglink_session_token';
const DIAGLINK_EXPIRES_KEY = 'diaglink_session_expires_at';

export function getDiagLinkSessionToken(): string | null {
  return sessionStorage.getItem(DIAGLINK_TOKEN_KEY);
}

export function getDiagLinkSessionExpiresAt(): string | null {
  return sessionStorage.getItem(DIAGLINK_EXPIRES_KEY);
}

export function setDiagLinkSession(sessionToken: string, expiresAtUtc: string): void {
  sessionStorage.setItem(DIAGLINK_TOKEN_KEY, sessionToken);
  sessionStorage.setItem(DIAGLINK_EXPIRES_KEY, expiresAtUtc);
}

export function clearDiagLinkSession(): void {
  sessionStorage.removeItem(DIAGLINK_TOKEN_KEY);
  sessionStorage.removeItem(DIAGLINK_EXPIRES_KEY);
}

export interface ApiAuthResult {
  headers: Record<string, string>;
  mode: 'diaglink' | 'microsoft';
}

/**
 * Single place deciding whether a protected API call authenticates via the DiagLink OTP
 * session (X-DiagLink-Session) or the Microsoft MSAL bearer token — never both at once.
 * DiagLink takes precedence whenever a session token is present in sessionStorage.
 */
export async function getApiAuthHeaders(
  getAccessToken: () => Promise<string | null>
): Promise<ApiAuthResult> {
  const diagLinkToken = getDiagLinkSessionToken();
  if (diagLinkToken) {
    return { headers: { 'X-DiagLink-Session': diagLinkToken }, mode: 'diaglink' };
  }

  const token = await getAccessToken();
  if (!token) {
    throw new Error('Failed to acquire access token');
  }
  return { headers: { Authorization: `Bearer ${token}` }, mode: 'microsoft' };
}
