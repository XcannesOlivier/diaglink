import React, { useCallback } from 'react';
import { Button, makeStyles } from '@fluentui/react-components';
import { Dismiss24Regular } from '@fluentui/react-icons';
import { CompanyFinancePanel } from './CompanyFinancePanel';
import { ViewRoot, ViewMessage } from './ViewLayout';
import { useApiResource } from '../../hooks/useApiResource';
import { getCompany } from '../../services/companyService';

interface CompanyViewProps {
  machine?: { id: string; name: string };
  onReturnToChat?: () => void;
  getAccessToken: () => Promise<string | null>;
  onDiagLinkSessionExpired?: () => void;
}

const useStyles = makeStyles({
  closeButton: {
    display: 'none',
    '@media (max-width: 1000px)': {
      display: 'inline-flex', minWidth: '44px', width: '44px', height: '44px', flexShrink: 0,
    },
  },
});

/** Company identity comes from GET /api/company; usage is scoped by the server. */
export const CompanyView: React.FC<CompanyViewProps> = ({ machine, getAccessToken, onDiagLinkSessionExpired, onReturnToChat }) => {
  const styles = useStyles();
  const fetchCompany = useCallback(() => getCompany(getAccessToken), [getAccessToken]);
  const state = useApiResource(fetchCompany, onDiagLinkSessionExpired);

  return (
    <ViewRoot title="Crédits et abonnement" subtitle={state.kind === 'success' ? state.data.name : undefined}
      headerAction={onReturnToChat && <Button className={styles.closeButton} appearance="subtle"
        icon={<Dismiss24Regular />} aria-label="Fermer et revenir au chat" title="Revenir au chat"
        onClick={onReturnToChat} />}>
      {state.kind === 'loading' && <ViewMessage loading message="Chargement des informations de l'entreprise..." />}
      {state.kind === 'unauthorized' && <ViewMessage message="Votre session a expiré. Veuillez vous reconnecter." />}
      {state.kind === 'forbidden' && <ViewMessage message="Accès non autorisé." />}
      {state.kind === 'not-found' && <ViewMessage message="Entreprise introuvable." />}
      {state.kind === 'error' && <ViewMessage message="Impossible de charger les données." />}
      {state.kind === 'success' && (
        <CompanyFinancePanel key={state.data.id} companyId={state.data.id} machine={machine} getAccessToken={getAccessToken} onDiagLinkSessionExpired={onDiagLinkSessionExpired} />
      )}
    </ViewRoot>
  );
};
