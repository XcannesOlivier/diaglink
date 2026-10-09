import { useEffect, useRef } from 'react';
import { fetchCompanyLogo } from '../services/companyBrandingService';
import { useAppState } from './useAppState';
import { useAuth } from './useAuth';

interface ActiveLogo {
  cacheKey: string;
  objectUrl: string;
}

/** Owns the single company-logo Object URL used by the authenticated application shell. */
export function useCompanyBrandingLogo(onDiagLinkSessionExpired?: () => void): void {
  const { auth, branding, dispatch } = useAppState();
  const { getAccessToken } = useAuth();
  const activeLogoRef = useRef<ActiveLogo | null>(
    branding.logoObjectUrl && branding.companyId
      ? {
          cacheKey: `${branding.companyId}:${branding.logoVersion ?? ''}`,
          objectUrl: branding.logoObjectUrl,
        }
      : null,
  );
  const currentUser = auth.currentUser;
  const companyId = currentUser?.companyId ?? null;
  const hasLogo = currentUser?.companyBranding?.hasLogo === true;
  const logoVersion = currentUser?.companyBranding?.logoVersion ?? null;
  const cacheKey = hasLogo && companyId ? `${companyId}:${logoVersion ?? ''}` : null;

  useEffect(() => {
    const releaseActiveLogo = () => {
      if (!activeLogoRef.current) return;
      URL.revokeObjectURL(activeLogoRef.current.objectUrl);
      activeLogoRef.current = null;
      dispatch({ type: 'COMPANY_LOGO_CLEARED' });
    };

    if (!cacheKey || !companyId) {
      releaseActiveLogo();
      return;
    }
    if (activeLogoRef.current?.cacheKey === cacheKey) {
      return;
    }

    releaseActiveLogo();
    const controller = new AbortController();
    let cancelled = false;

    void fetchCompanyLogo(getAccessToken, controller.signal).then(result => {
      if (cancelled) return;
      if (result.diagLinkSessionExpired) {
        onDiagLinkSessionExpired?.();
        return;
      }
      if (!result.logo) return;

      const objectUrl = URL.createObjectURL(result.logo);
      if (cancelled) {
        URL.revokeObjectURL(objectUrl);
        return;
      }
      activeLogoRef.current = { cacheKey, objectUrl };
      dispatch({ type: 'COMPANY_LOGO_LOADED', companyId, logoVersion, logoObjectUrl: objectUrl });
    });

    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [cacheKey, companyId, dispatch, getAccessToken, logoVersion, onDiagLinkSessionExpired]);

  useEffect(() => () => {
    if (!activeLogoRef.current) return;
    URL.revokeObjectURL(activeLogoRef.current.objectUrl);
    activeLogoRef.current = null;
    dispatch({ type: 'COMPANY_LOGO_CLEARED' });
  }, [dispatch]);
}
