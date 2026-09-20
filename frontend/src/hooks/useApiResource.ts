import { useEffect, useState } from 'react';
import type { ApiResult } from '../types/apiResult';

/** UI-facing state for any read-only view backed by an ApiResult-returning fetch. */
export type ViewState<T> =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'success'; data: T }
  | { kind: 'unauthorized' }
  | { kind: 'forbidden' }
  | { kind: 'not-found' }
  | { kind: 'error' };

/**
 * Runs `fetcher` on mount (and whenever it changes — callers must memoize it with useCallback)
 * and exposes a simple loading/success/error state. On a DiagLink-session 401, notifies the caller
 * via `onDiagLinkSessionExpired` so session cleanup stays centralized (see App.tsx).
 * Pass `enabled = false` to skip fetching entirely (e.g. an endpoint that shouldn't be called for
 * a given role) — state stays `idle`.
 */
export function useApiResource<T>(
  fetcher: () => Promise<ApiResult<T>>,
  onDiagLinkSessionExpired?: () => void,
  enabled = true
): ViewState<T> {
  const [state, setState] = useState<ViewState<T>>({ kind: enabled ? 'loading' : 'idle' });

  useEffect(() => {
    if (!enabled) {
      setState({ kind: 'idle' });
      return;
    }

    let cancelled = false;
    setState({ kind: 'loading' });

    fetcher().then(result => {
      if (cancelled) return;

      switch (result.kind) {
        case 'success':
          setState({ kind: 'success', data: result.data });
          break;
        case 'unauthorized':
          setState({ kind: 'unauthorized' });
          if (result.diagLinkSessionExpired) {
            onDiagLinkSessionExpired?.();
          }
          break;
        default:
          setState({ kind: result.kind });
          break;
      }
    });

    return () => {
      cancelled = true;
    };
  }, [fetcher, enabled, onDiagLinkSessionExpired]);

  return state;
}
