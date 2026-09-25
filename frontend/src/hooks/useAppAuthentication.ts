import { useCallback, useEffect, useState } from 'react';
import { useMsal } from '@azure/msal-react';
import { useAppState } from './useAppState';
import { useAuth } from './useAuth';
import { fetchCurrentUser } from '../services/currentUserService';
import { clearDiagLinkSession, getDiagLinkSessionExpiresAt, getDiagLinkSessionToken, setDiagLinkSession } from '../utils/apiAuth';

export function useAppAuthentication() {
  const { accounts } = useMsal();
  const { auth, dispatch } = useAppState();
  const { getAccessToken } = useAuth();
  const [email, setEmail] = useState(() => localStorage.getItem('diaglink-last-email') ?? '');
  const [emailCheckMessage, setEmailCheckMessage] = useState<string | null>(null);
  const [isCheckingEmail, setIsCheckingEmail] = useState(false);
  const [showCodeStep, setShowCodeStep] = useState(false);
  const [code, setCode] = useState('');
  const [diagLinkSessionCreated, setDiagLinkSessionCreated] = useState(false);
  const [isCheckingSession, setIsCheckingSession] = useState(true);

  const expireDiagLinkSession = useCallback(() => {
    clearDiagLinkSession();
    setDiagLinkSessionCreated(false);
    dispatch({ type: 'AUTH_CURRENT_USER_CLEARED' });
  }, [dispatch]);

  const loadCurrentUser = useCallback(async () => {
    const { currentUser, diagLinkSessionExpired } = await fetchCurrentUser(getAccessToken);
    if (currentUser) {
      dispatch({ type: 'AUTH_CURRENT_USER_LOADED', currentUser });
    } else if (diagLinkSessionExpired) {
      expireDiagLinkSession();
    }
  }, [dispatch, expireDiagLinkSession, getAccessToken]);

  useEffect(() => {
    if (accounts.length > 0 && !auth.currentUser) void loadCurrentUser();
  }, [accounts.length, auth.currentUser, loadCurrentUser]);

  useEffect(() => {
    const token = getDiagLinkSessionToken();
    const expiresAt = getDiagLinkSessionExpiresAt();
    if (!token || !expiresAt || new Date(expiresAt).getTime() <= Date.now()) {
      if (token || expiresAt) clearDiagLinkSession();
      setIsCheckingSession(false);
      return;
    }

    let cancelled = false;
    const validateSession = async () => {
      try {
        const apiUrl = import.meta.env.VITE_API_URL || '/api';
        const response = await fetch(`${apiUrl}/auth/validate-session`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ sessionToken: token }),
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        const data = await response.json();
        if (cancelled) return;
        if (data.valid) {
          setDiagLinkSessionCreated(true);
          await loadCurrentUser();
        } else {
          clearDiagLinkSession();
        }
      } catch (error) {
        console.error('Error validating DiagLink session:', error);
      } finally {
        if (!cancelled) setIsCheckingSession(false);
      }
    };
    void validateSession();
    return () => { cancelled = true; };
  }, [loadCurrentUser]);

  const handleContinue = useCallback(async () => {
    const trimmedEmail = email.trim();
    if (!trimmedEmail) return;
    localStorage.setItem('diaglink-last-email', trimmedEmail);
    setIsCheckingEmail(true);
    setEmailCheckMessage(null);
    try {
      const apiUrl = import.meta.env.VITE_API_URL || '/api';
      const checkResponse = await fetch(`${apiUrl}/auth/check-email`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: trimmedEmail }),
      });
      if (!checkResponse.ok) throw new Error(`HTTP ${checkResponse.status}: ${checkResponse.statusText}`);
      const checkData = await checkResponse.json();
      if (!checkData.known) {
        setEmailCheckMessage('Utilisateur inconnu');
        return;
      }
      const codeResponse = await fetch(`${apiUrl}/auth/request-code`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: trimmedEmail }),
      });
      if (!codeResponse.ok) throw new Error(`HTTP ${codeResponse.status}: ${codeResponse.statusText}`);
      const codeData = await codeResponse.json();
      if (codeData.success) setShowCodeStep(true);
      else setEmailCheckMessage("Impossible d'envoyer le code de connexion.");
    } catch (error) {
      console.error('Error requesting login code:', error);
      setEmailCheckMessage("Impossible d'envoyer le code de connexion. Veuillez réessayer.");
    } finally {
      setIsCheckingEmail(false);
    }
  }, [email]);

  const handleVerifyCode = useCallback(async (codeToVerify?: string): Promise<boolean> => {
    const trimmedEmail = email.trim();
    const trimmedCode = (codeToVerify ?? code).trim();
    if (!trimmedEmail || trimmedCode.length !== 6) return false;
    try {
      const apiUrl = import.meta.env.VITE_API_URL || '/api';
      const response = await fetch(`${apiUrl}/auth/verify-code`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: trimmedEmail, code: trimmedCode }),
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      const data = await response.json();
      if (data.success && data.sessionToken && data.expiresAtUtc) {
        setDiagLinkSession(data.sessionToken, data.expiresAtUtc);
        setDiagLinkSessionCreated(true);
        await loadCurrentUser();
        return true;
      }
      alert('Code incorrect ou expiré');
    } catch (error) {
      console.error('Error verifying login code:', error);
      alert('Impossible de vérifier le code.');
    }
    return false;
  }, [code, email, loadCurrentUser]);

  const handleChangeEmail = useCallback(() => {
    setShowCodeStep(false);
    setCode('');
    setEmailCheckMessage(null);
  }, []);

  return { isAuthenticated: accounts.length > 0 || diagLinkSessionCreated, isCheckingSession, email, setEmail, emailCheckMessage, isCheckingEmail, showCodeStep, code, setCode, handleContinue, handleVerifyCode, handleChangeEmail, expireDiagLinkSession };
}
