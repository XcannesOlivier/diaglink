import { UnauthenticatedTemplate } from "@azure/msal-react";
import { Button, Input, Spinner } from '@fluentui/react-components';
import { useAppState } from './hooks/useAppState';
import { ErrorBoundary } from "./components/core/ErrorBoundary";
import { AppShell } from "./components/layout/AppShell";
import { useState, useEffect, useCallback } from "react";
import { useAuth } from "./hooks/useAuth";
import type { IAgentMetadata } from "./types/chat";
import { getApiAuthHeaders, setDiagLinkSession, getDiagLinkSessionToken, getDiagLinkSessionExpiresAt, clearDiagLinkSession } from "./utils/apiAuth";
import { fetchCurrentUser } from "./services/currentUserService";
import { getMachines } from './services/machineService';
import logoDiagLink from "./assets/Logo DiagLink.png";
import { AIFoundryLogo } from "./components/icons/AIFoundryLogo";
import authStyles from "./App.module.css";
import "./App.css";

function App() {
  const { auth, dispatch, state } = useAppState();
  const { getAccessToken } = useAuth();
  const [agentMetadata, setAgentMetadata] = useState<IAgentMetadata | null>(null);
  const [isLoadingAgent, setIsLoadingAgent] = useState(true);
  const [email, setEmail] = useState(() => localStorage.getItem('diaglink-last-email') ?? '');
  const [emailCheckMessage, setEmailCheckMessage] = useState<string | null>(null);
  const [isCheckingEmail, setIsCheckingEmail] = useState(false);
  const [showCodeStep, setShowCodeStep] = useState(false);
  const [code, setCode] = useState('');
  // Session DiagLink OTP (distincte de l'auth MSAL) — combinée à auth.status pour débloquer le chat.
  const [diagLinkSessionCreated, setDiagLinkSessionCreated] = useState(false);
  const [hasNoMachines, setHasNoMachines] = useState(false);

  // Vrai si l'utilisateur est connecté via Microsoft (MSAL) OU via une session DiagLink validée.
  // Ne modifie jamais auth.status lui-même — les deux mécanismes restent distincts.
  const isAppAuthenticated = auth.status === 'authenticated' || diagLinkSessionCreated;

  // Session DiagLink invalidée côté serveur (401) pendant l'utilisation du chat — retour à l'écran de connexion.
  const handleDiagLinkSessionExpired = useCallback(() => {
    clearDiagLinkSession();
    setDiagLinkSessionCreated(false);
    dispatch({ type: 'AUTH_CURRENT_USER_CLEARED' });
  }, [dispatch]);

  // Résout Role/CompanyId via GET /api/auth/me (jamais déduits côté client) et les place dans l'état
  // global — appelé après une session DiagLink nouvellement créée ou restaurée. Un échec dû à une
  // session DiagLink expirée déclenche le même nettoyage qu'un 401 en cours de chat.
  const loadCurrentUser = useCallback(async () => {
    const { currentUser, diagLinkSessionExpired } = await fetchCurrentUser(getAccessToken);

    if (currentUser) {
      dispatch({ type: 'AUTH_CURRENT_USER_LOADED', currentUser });
      return;
    }

    if (diagLinkSessionExpired) {
      handleDiagLinkSessionExpired();
    }
  }, [getAccessToken, dispatch, handleDiagLinkSessionExpired]);

  // Auth MSAL : `auth.status` passe à 'authenticated' dès que MSAL a un compte, mais
  // currentUser (rôle/entreprise) n'est chargé qu'ici, via /api/auth/me — sans quoi
  // AppShell rend un nav/badge vides (currentUser == null) et donne l'impression de
  // retomber sur l'ancien écran de chat direct.
  useEffect(() => {
    if (auth.status === 'authenticated' && !auth.currentUser) {
      void loadCurrentUser();
    }
  }, [auth.status, auth.currentUser, loadCurrentUser]);

  const handleContinue = useCallback(async () => {
  const trimmedEmail = email.trim();
  if (!trimmedEmail) return;

  localStorage.setItem('diaglink-last-email', trimmedEmail);

  setIsCheckingEmail(true);
  setEmailCheckMessage(null);

  try {
    const apiUrl = import.meta.env.VITE_API_URL || '/api';

    // 1. Vérifier que l'utilisateur existe
    const checkResponse = await fetch(`${apiUrl}/auth/check-email`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: trimmedEmail })
    });

    if (!checkResponse.ok) {
      throw new Error(`HTTP ${checkResponse.status}: ${checkResponse.statusText}`);
    }

    const checkData = await checkResponse.json();

    if (!checkData.known) {
      setEmailCheckMessage('Utilisateur inconnu');
      return;
    }

    // 2. Demander réellement l'envoi du code
    const codeResponse = await fetch(`${apiUrl}/auth/request-code`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: trimmedEmail })
    });

    if (!codeResponse.ok) {
      throw new Error(`HTTP ${codeResponse.status}: ${codeResponse.statusText}`);
    }

    const codeData = await codeResponse.json();

    if (codeData.success) {
      setShowCodeStep(true);
    } else {
      setEmailCheckMessage("Impossible d'envoyer le code de connexion.");
    }

  } catch (error) {
    console.error('Error requesting login code:', error);
    setEmailCheckMessage(
      "Impossible d'envoyer le code de connexion. Veuillez réessayer."
    );
  } finally {
    setIsCheckingEmail(false);
  }
}, [email]);

  const handleChangeEmail = useCallback(() => {
  setShowCodeStep(false);
  setCode('');
  setEmailCheckMessage(null);
}, []);

  // Restauration d'une session DiagLink existante au chargement — le backend reste
  // seul juge de validité, sessionStorage n'est qu'un indice local à confirmer.
  useEffect(() => {
    const token = getDiagLinkSessionToken();
    const expiresAt = getDiagLinkSessionExpiresAt();

    if (!token || !expiresAt) {
      return;
    }

    if (new Date(expiresAt).getTime() <= Date.now()) {
      clearDiagLinkSession();
      return;
    }

    let cancelled = false;

    (async () => {
      try {
        const apiUrl = import.meta.env.VITE_API_URL || '/api';
        const response = await fetch(`${apiUrl}/auth/validate-session`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ sessionToken: token })
        });

        if (!response.ok) {
          throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        const data = await response.json();

        if (cancelled) return;

        if (data.valid) {
          setDiagLinkSessionCreated(true);
          void loadCurrentUser();
        } else {
          clearDiagLinkSession();
        }
      } catch (error) {
        console.error('Error validating DiagLink session:', error);
        // Échec technique = session non confirmée, on ne l'accepte pas.
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [loadCurrentUser]);

  const handleVerifyCode = useCallback(async (codeToVerify?: string) => {
  const trimmedEmail = email.trim();
  const trimmedCode = (codeToVerify ?? code).trim();

  if (!trimmedEmail || trimmedCode.length !== 6) return;


  try {
    const apiUrl = import.meta.env.VITE_API_URL || '/api';

    const response = await fetch(`${apiUrl}/auth/verify-code`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        email: trimmedEmail,
        code: trimmedCode
      })
    });

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    }

    const data = await response.json();

    if (data.success && data.sessionToken && data.expiresAtUtc) {
      setDiagLinkSession(data.sessionToken, data.expiresAtUtc);
      setDiagLinkSessionCreated(true);
      void loadCurrentUser();
    } else {
      alert('Code incorrect ou expiré');
    }
  } catch (error) {
    console.error('Error verifying login code:', error);
    alert('Impossible de vérifier le code.');
  }
}, [email, code, loadCurrentUser]);
  
  // Wrap fetchAgentMetadata in useCallback to make it stable for the effect
  const fetchAgentMetadata = useCallback(async () => {
    if (!isAppAuthenticated) return;
    if (!state.machine.selected) return;

    try {
      const { headers, mode } = await getApiAuthHeaders(getAccessToken);
      const apiUrl = import.meta.env.VITE_API_URL || '/api';

      const response = await fetch(`${apiUrl}/agent?machineId=${encodeURIComponent(state.machine.selected.id)}`, {
        headers: {
          ...headers,
          'Content-Type': 'application/json'
       }
     });

      if (!response.ok) {
        if (response.status === 401 && mode === 'diaglink') {
          handleDiagLinkSessionExpired();
          return;
        }
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }

      const data = await response.json();
      setAgentMetadata(data);
      
      // Update document title with agent name
      document.title = data.name ? `${data.name} - Assistant Technique` : 'Assistant Technique';
    } catch (error) {
      console.error('Error fetching agent metadata:', error);
      // Fallback data keeps UI functional on error
      setAgentMetadata({
        id: 'fallback-agent',
        object: 'agent',
        createdAt: Date.now() / 1000,
        name: 'Assistant Technique',
        description: 'Votre support pour diagnostiquer, localiser et intervenir plus vite',
        model: 'gpt-4o-mini',
        metadata: { logo: 'Avatar_Default.svg' }
      });
      document.title = 'Assistant Technique';
    } finally {
      setIsLoadingAgent(false);
    }
  }, [isAppAuthenticated, state.machine.selected, getAccessToken, handleDiagLinkSessionExpired]);

  useEffect(() => {
    fetchAgentMetadata();
  }, [fetchAgentMetadata]);

  // Sélectionne automatiquement la première machine accessible si aucune n'est déjà active.
  const initializeDefaultMachine = useCallback(async () => {
    if (!isAppAuthenticated) return;
    if (state.machine.selected) return;

    const result = await getMachines(getAccessToken);

    if (result.kind === 'success' && result.data.length === 0) {
      setHasNoMachines(true);
      return;
    }

    if (result.kind === 'success' && result.data.length > 0) {
      const firstMachine = result.data[0];
      dispatch({
        type: 'MACHINE_SELECT',
        machine: {
          id: firstMachine.id,
          name: firstMachine.name,
          reference: firstMachine.reference ?? null
        }
      });
      setHasNoMachines(false);
    }
  }, [isAppAuthenticated, state.machine.selected, getAccessToken, dispatch]);

  useEffect(() => {
    void initializeDefaultMachine();
  }, [initializeDefaultMachine]);

  return (
    <ErrorBoundary>
      {isAppAuthenticated ? (
        <>
          {hasNoMachines ? (
            <div className="app-container" style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              height: '100vh',
              flexDirection: 'column',
              gap: '0.5rem',
              textAlign: 'center'
            }}>
              <h2 style={{ margin: 0 }}>Aucune machine disponible</h2>
              <p style={{ margin: 0, color: 'var(--colorNeutralForeground3, #666)' }}>
                Aucune machine ne vous est actuellement assignée.
              </p>
            </div>
          ) : isLoadingAgent ? (
            <div className="app-container" style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              height: '100vh',
              flexDirection: 'column',
              gap: '1rem'
            }}>
              <Spinner size="large" />
              <p style={{ margin: 0 }}>Chargement...</p>
            </div>
          ) : (
            agentMetadata && (
              <div className="app-container">
                <AppShell
                  agentId={agentMetadata.id}
                  agentName={
  state.machine.selected
    ? `Assistant-Technique-${state.machine.selected.name}`
    : agentMetadata.name
}
                  agentDescription={agentMetadata.description || undefined}
                  agentLogo={agentMetadata.metadata?.logo}
                  starterPrompts={agentMetadata.starterPrompts || undefined}
                  onDiagLinkSessionExpired={handleDiagLinkSessionExpired}
                />
              </div>
            )
          )}
        </>
      ) : (
        <UnauthenticatedTemplate>
          <div className={authStyles.authScreen}>
            <div className={authStyles.authCard}>
              <img
                src={logoDiagLink}
                alt="DiagLink"
                className={authStyles.authLogo}
              />

              <h1 className={authStyles.authTitle}>
                Assistant Technique
              </h1>

              <p className={authStyles.authSubtitle}>
                Votre support technique pour la maintenance industrielle
              </p>

              <p className={authStyles.authDescription}>
                Accédez rapidement aux informations de vos équipements et facilitez vos diagnostics.
              </p>

              {!showCodeStep ? (
                <>
                  <div className={authStyles.fieldGroup}>
                    <label className={authStyles.fieldLabel}>
                      Adresse e-mail
                    </label>
                    <Input
                      type="email"
                      name="email"
                      autoComplete="email"
                      inputMode="email"
                      size="large"
                      value={email}
                      onChange={(_, data) => setEmail(data.value)}
                      className={authStyles.fullWidthInput}
                    />
                    {emailCheckMessage && (
                      <p className={authStyles.fieldMessage}>
                        {emailCheckMessage}
                      </p>
                    )}
                  </div>

                  <Button
                    appearance="primary"
                    size="large"
                    disabled={!email.trim() || isCheckingEmail}
                    onClick={handleContinue}
                    className={authStyles.primaryButton}
                  >
                    Continuer
                  </Button>
                </>
              ) : (
                <>
                  <p className={authStyles.otpIntro}>
                    Un code de connexion a été envoyé à<br /><strong>{email}</strong>
                  </p>

                  <div className={authStyles.fieldGroup}>
                    <label className={authStyles.fieldLabel}>
                      Code de connexion
                    </label>
                    <Input
                      type="text"
                      inputMode="numeric"
                      autoComplete="one-time-code"
                      maxLength={6}
                      size="large"
                      value={code}
                      onChange={(_, data) => {
                        const newCode = data.value.replace(/\D/g, '').slice(0, 6);

                        setCode(newCode);

                        if (newCode.length === 6) {
                          if (window.matchMedia('(max-width: 1000px)').matches) {
                            setTimeout(() => {
                              if (document.activeElement instanceof HTMLElement) {
                                document.activeElement.blur();
                              }
                            }, 100);
                          }

                         void handleVerifyCode(newCode);
                        }
                      }}
                      className={authStyles.fullWidthInput}
                    />
                  </div>

                  <Button
                    appearance="primary"
                    size="large"
                    onClick={() => void handleVerifyCode()}
                    disabled={code.length !== 6}
                    className={authStyles.primaryButton}
                  >
                    Se connecter
                  </Button>

                  <Button
                    appearance="transparent"
                    size="small"
                    onClick={handleChangeEmail}
                    className={authStyles.changeEmailButton}
                  >
                    Changer d'adresse e-mail
                  </Button>
                </>
              )}

              <p className={authStyles.secureNote}>
                Connexion sécurisée
              </p>

              <div className={authStyles.poweredBy}>
                <AIFoundryLogo className={authStyles.foundryLogo} width={16} height={16} />
                <span><span className={authStyles.poweredByText}>Propulsé par </span>Microsoft Foundry</span>
              </div>
            </div>
          </div>
        </UnauthenticatedTemplate>
      )}
    </ErrorBoundary>
  );

}
export default App;