import { AuthenticatedTemplate, UnauthenticatedTemplate, useMsal } from "@azure/msal-react";
import { Button, Spinner } from '@fluentui/react-components';
import { useAppState } from './hooks/useAppState';
import { ErrorBoundary } from "./components/core/ErrorBoundary";
import { AgentChat } from "./components/AgentChat";
import { loginRequest } from "./config/authConfig";
import { useState, useEffect, useCallback } from "react";
import { useAuth } from "./hooks/useAuth";
import type { IAgentMetadata } from "./types/chat";
import logoDiagLink from "./assets/Logo DiagLink.png";
import "./App.css";

function App() {
  const { instance } = useMsal();
  const { auth } = useAppState();
  const { getAccessToken } = useAuth();
  const [agentMetadata, setAgentMetadata] = useState<IAgentMetadata | null>(null);
  const [isLoadingAgent, setIsLoadingAgent] = useState(true);

  // Wrap fetchAgentMetadata in useCallback to make it stable for the effect
  const fetchAgentMetadata = useCallback(async () => {
    if (auth.status !== 'authenticated') return;

    try {
      const token = await getAccessToken();
      const apiUrl = import.meta.env.VITE_API_URL || '/api';
      
      const response = await fetch(`${apiUrl}/agent`, {
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      });

      if (!response.ok) {
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
  }, [auth.status, getAccessToken]);

  useEffect(() => {
    fetchAgentMetadata();
  }, [fetchAgentMetadata]);

  return (
    <ErrorBoundary>
      {auth.status === 'authenticated' ? (
        <>
          {isLoadingAgent ? (
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
            <AuthenticatedTemplate>
              {agentMetadata && (
                <div className="app-container">
                  <AgentChat 
                    agentId={agentMetadata.id}
                    agentName={agentMetadata.name}
                    agentDescription={agentMetadata.description || undefined}
                    agentLogo={agentMetadata.metadata?.logo}
                    starterPrompts={agentMetadata.starterPrompts || undefined}
                  />
                </div>
              )}
            </AuthenticatedTemplate>
          )}
        </>
      ) : (
        <UnauthenticatedTemplate>
          <div className="app-container" style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            minHeight: '100vh',
            padding: '32px 24px',
            background: '#f5f5f5',
            fontFamily: 'Segoe UI, Segoe UI Variable, sans-serif'
          }}>
            <div style={{
              width: '100%',
              maxWidth: '440px',
              background: '#ffffff',
              border: '1px solid #e5e5e5',
              borderRadius: '16px',
              boxShadow: '0 1px 2px rgba(0, 0, 0, 0.04), 0 8px 24px rgba(0, 0, 0, 0.02)',
              padding: '28px 28px 20px',
              textAlign: 'center'
            }}>
              <img
                src={logoDiagLink}
                alt="DiagLink"
                style={{
                  height: '120px',
                  width: 'auto',
                  display: 'block',
                  margin: '0 auto 12px',
                  objectFit: 'contain',
                  opacity: 0.94
                }}
              />

              <h1 style={{
                margin: '0 0 8px',
                fontSize: '2rem',
                lineHeight: 1.2,
                color: '#1a1a1a',
                fontWeight: 600,
                letterSpacing: '-0.04em'
              }}>
                Assistant Technique
              </h1>

              <p style={{
                margin: '0 0 16px',
                color: '#5c5c5c',
                fontSize: '1rem',
                lineHeight: 1.5,
                fontWeight: 400
              }}>
                Votre assistant IA pour la maintenance industrielle
              </p>

              <p style={{
                margin: '0 auto 24px',
                maxWidth: '330px',
                color: '#3b3b3b',
                fontSize: '0.96rem',
                lineHeight: 1.6,
                fontWeight: 500
              }}>
                Retrouvez rapidement les informations utiles à vos équipements et accélérez vos diagnostics.
              </p>

              <Button
                appearance="primary"
                size="large"
                onClick={() => instance.loginRedirect(loginRequest)}
                style={{
                  width: '100%',
                  minHeight: '46px',
                  backgroundColor: '#0078d4',
                  borderColor: '#0078d4',
                  color: '#ffffff',
                  fontWeight: 600,
                  borderRadius: '8px',
                  boxShadow: 'none',
                  padding: '0 18px'
                }}
              >
                Se connecter avec Microsoft
              </Button>

              <p style={{
                margin: '16px 0 0',
                color: '#666666',
                fontSize: '0.78rem',
                lineHeight: 1.5
              }}>
                Authentification sécurisée par Microsoft Entra ID
              </p>

              <div style={{
                marginTop: '34px',
                paddingTop: '16px',
                borderTop: '1px solid #efefef',
                color: '#8a8a8a',
                fontSize: '0.74rem',
                letterSpacing: '0.01em'
              }}>
                Propulsé par Microsoft Foundry
              </div>
            </div>
          </div>
        </UnauthenticatedTemplate>
      )}
    </ErrorBoundary>
  );
}

export default App;
