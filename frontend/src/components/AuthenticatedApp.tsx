import { Spinner } from '@fluentui/react-components';
import { useCallback, useEffect, useState } from 'react';
import { AppShell } from './layout/AppShell';
import { useAppState } from '../hooks/useAppState';
import { useAuth } from '../hooks/useAuth';
import { getApiAuthHeaders } from '../utils/apiAuth';
import { getMachines } from '../services/machineService';
import type { IAgentMetadata } from '../types/chat';

interface AuthenticatedAppProps {
  loadingOnly?: boolean;
  onDiagLinkSessionExpired?: () => void;
}

export function AuthenticatedApp({ loadingOnly = false, onDiagLinkSessionExpired }: AuthenticatedAppProps) {
  const { state, dispatch } = useAppState();
  const { getAccessToken } = useAuth();
  const [agentMetadata, setAgentMetadata] = useState<IAgentMetadata | null>(null);
  const [isLoadingAgent, setIsLoadingAgent] = useState(true);
  const [hasNoMachines, setHasNoMachines] = useState(false);

  const fetchAgentMetadata = useCallback(async () => {
    if (loadingOnly || !state.machine.selected) return;
    try {
      const { headers, mode } = await getApiAuthHeaders(getAccessToken);
      const apiUrl = import.meta.env.VITE_API_URL || '/api';
      const response = await fetch(`${apiUrl}/agent?machineId=${encodeURIComponent(state.machine.selected.id)}`, {
        headers: { ...headers, 'Content-Type': 'application/json' },
      });
      if (!response.ok) {
        if (response.status === 401 && mode === 'diaglink') {
          onDiagLinkSessionExpired?.();
          return;
        }
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }
      const data = await response.json();
      setAgentMetadata(data);
      document.title = data.name ? `${data.name} - Assistant Technique` : 'Assistant Technique';
    } catch (error) {
      console.error('Error fetching agent metadata:', error);
      setAgentMetadata({
        id: 'fallback-agent', object: 'agent', createdAt: Date.now() / 1000,
        name: 'Assistant Technique', description: 'Votre support pour diagnostiquer, localiser et intervenir plus vite',
        model: 'gpt-4o-mini', metadata: { logo: 'Avatar_Default.svg' },
        capabilities: { imageAttachments: false, fileAttachments: false },
      });
      document.title = 'Assistant Technique';
    } finally {
      setIsLoadingAgent(false);
    }
  }, [getAccessToken, loadingOnly, onDiagLinkSessionExpired, state.machine.selected]);

  useEffect(() => { void fetchAgentMetadata(); }, [fetchAgentMetadata]);

  const initializeDefaultMachine = useCallback(async () => {
    if (loadingOnly || state.machine.selected) return;
    const result = await getMachines(getAccessToken);
    if (result.kind === 'success' && result.data.length === 0) {
      setHasNoMachines(true);
    } else if (result.kind === 'success' && result.data.length > 0) {
      const firstMachine = result.data[0];
      dispatch({ type: 'MACHINE_SELECT', machine: { id: firstMachine.id, name: firstMachine.name, reference: firstMachine.reference ?? null } });
      setHasNoMachines(false);
    }
  }, [dispatch, getAccessToken, loadingOnly, state.machine.selected]);

  useEffect(() => { void initializeDefaultMachine(); }, [initializeDefaultMachine]);

  if (loadingOnly || isLoadingAgent) {
    return <div className="app-container app-loading"><Spinner size="large" /><p>Chargement...</p></div>;
  }
  if (hasNoMachines) {
    return <div className="app-container app-loading"><h2>Aucune machine disponible</h2><p>Aucune machine ne vous est actuellement assignée.</p></div>;
  }
  if (!agentMetadata) return null;

  return (
    <div className="app-container">
      <AppShell
        agentId={agentMetadata.id}
        agentName={state.machine.selected ? `Assistant-Technique-${state.machine.selected.name}` : agentMetadata.name}
        agentDescription={agentMetadata.description || undefined}
        agentLogo={agentMetadata.metadata?.logo}
        starterPrompts={agentMetadata.starterPrompts || undefined}
        onDiagLinkSessionExpired={onDiagLinkSessionExpired}
      />
    </div>
  );
}
