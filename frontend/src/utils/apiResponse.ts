import type { ApiResult, ApiWriteResult } from '../types/apiResult';

/**
 * Turns a fetch Response into an ApiResult — the single place that maps HTTP status codes to the
 * outcomes the UI cares about. `mode` scopes 401 handling the same way chatService/currentUserService
 * already do: only a DiagLink session is considered "expired" and worth logging out for.
 */
export async function parseApiResult<T>(
  response: Response,
  mode: 'diaglink' | 'microsoft'
): Promise<ApiResult<T>> {
  if (response.status === 401) {
    return { kind: 'unauthorized', diagLinkSessionExpired: mode === 'diaglink' };
  }
  if (response.status === 403) {
    return { kind: 'forbidden' };
  }
  if (response.status === 404) {
    return { kind: 'not-found' };
  }
  if (!response.ok) {
    return { kind: 'error' };
  }

  try {
    const data = (await response.json()) as T;
    return { kind: 'success', data };
  } catch {
    return { kind: 'error' };
  }
}

async function readErrorMessage(response: Response, fallback: string): Promise<string> {
  try {
    const body = (await response.json()) as { error?: string };
    return body.error ?? fallback;
  } catch {
    return fallback;
  }
}

/** Same mapping as parseApiResult, plus the 400/409 outcomes only mutations can return. */
export async function parseApiWriteResult<T>(
  response: Response,
  mode: 'diaglink' | 'microsoft'
): Promise<ApiWriteResult<T>> {
  if (response.status === 401) {
    return { kind: 'unauthorized', diagLinkSessionExpired: mode === 'diaglink' };
  }
  if (response.status === 403) {
    return { kind: 'forbidden' };
  }
  if (response.status === 404) {
    return { kind: 'not-found' };
  }
  if (response.status === 400) {
    return { kind: 'validation-error', message: await readErrorMessage(response, 'Requête invalide.') };
  }
  if (response.status === 409) {
    return { kind: 'conflict', message: await readErrorMessage(response, 'Conflit détecté.') };
  }
  if (!response.ok) {
    return { kind: 'error' };
  }

  // 204 (and any other empty-body success) — nothing to parse, e.g. DELETE /machines/{id}.
  if (response.status === 204) {
    return { kind: 'success', data: null as T };
  }

  try {
    const data = (await response.json()) as T;
    return { kind: 'success', data };
  } catch {
    return { kind: 'error' };
  }
}
