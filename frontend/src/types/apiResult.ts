/**
 * Discriminated result for read-only API calls — mirrors the possible outcomes of any GET call
 * to the DiagLink backend (2xx / 401 / 403 / 404 / network or unexpected error). Components never
 * see a raw `Response` — they only ever branch on `kind`.
 */
export type ApiResult<T> =
  | { kind: 'success'; data: T }
  | { kind: 'unauthorized'; diagLinkSessionExpired: boolean }
  | { kind: 'forbidden' }
  | { kind: 'not-found' }
  | { kind: 'error' };

/**
 * Discriminated result for write (POST/PUT/PATCH/DELETE) API calls — adds the two outcomes a mutation
 * can hit that a GET never does: 400 (validation) and 409 (conflict), both carrying the server's message.
 */
export type ApiWriteResult<T> =
  | { kind: 'success'; data: T }
  | { kind: 'validation-error'; message: string }
  | { kind: 'conflict'; message: string }
  | { kind: 'unauthorized'; diagLinkSessionExpired: boolean }
  | { kind: 'forbidden' }
  | { kind: 'not-found' }
  | { kind: 'error' };
