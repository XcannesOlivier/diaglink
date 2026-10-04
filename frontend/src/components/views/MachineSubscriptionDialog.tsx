import { useRef, useState } from 'react';
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { setCompanyStripeMachineStatus, setStripeMachineStatus } from '../../services/stripeAdminService';
import { DialogCloseButton } from '../core/DialogCloseButton';

const useStyles = makeStyles({
  content: { display: 'grid', gap: tokens.spacingVerticalM },
  heading: { margin: 0 },
  explanation: { color: tokens.colorNeutralForeground2, lineHeight: tokens.lineHeightBase400 },
  error: { color: tokens.colorPaletteRedForeground1 },
  destructive: { color: tokens.colorPaletteRedForeground1 },
  companyActions: {
    '@media (max-width: 767px)': {
      display: 'flex',
      flexDirection: 'row',
      justifyContent: 'flex-end',
      alignItems: 'center',
      gap: '8px',
      width: '100%',
      '> button': {
        flex: '0 1 auto',
        minWidth: 0,
        maxWidth: '100%',
        paddingInline: tokens.spacingHorizontalS,
        whiteSpace: 'normal',
      },
    },
  },
});

interface ManagedMachine {
  id: string;
  name: string;
  billable: boolean;
  hasPaidRights: boolean;
}

export function MachineSubscriptionDialog({ open, machine, companyId, token, actionEnabled, companyScoped=false, onOpenChange, onSuccess, onDiagLinkSessionExpired }: {
  open: boolean;
  machine: ManagedMachine | null;
  companyId: string;
  token: () => Promise<string | null>;
  actionEnabled: boolean;
  companyScoped?: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: () => void;
  onDiagLinkSessionExpired?: () => void;
}) {
  const styles = useStyles();
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const inFlight = useRef(false);
  const action = !machine?.hasPaidRights ? 'reactivate' : machine.billable ? 'cancel' : 'renew';

  const close = () => {
    if (inFlight.current) return;
    setConfirming(false);
    setError('');
    onOpenChange(false);
  };

  const changeSubscription = async () => {
    if (!machine || !actionEnabled || inFlight.current) return;
    const active = action !== 'cancel';
    const key = `diaglink:machine-status:${companyId}:${machine.id}`;
    let pending: { active: boolean; id: string };
    try {
      const stored = JSON.parse(localStorage.getItem(key) || 'null') as { active?: boolean; id?: string } | null;
      pending = stored?.active === active && typeof stored.id === 'string'
        ? { active, id: stored.id }
        : { active, id: crypto.randomUUID() };
      localStorage.setItem(key, JSON.stringify(pending));
    } catch {
      setError('Stockage indisponible : impossible de sécuriser la reprise.');
      return;
    }

    inFlight.current = true;
    setBusy(true);
    setError('');
    const result = companyScoped
      ? await setCompanyStripeMachineStatus(token, machine.id, active, pending.id)
      : await setStripeMachineStatus(token, companyId, machine.id, active, pending.id);
    inFlight.current = false;
    setBusy(false);
    if (result.kind === 'success') {
      if (result.data.status !== 'AwaitingPayment') localStorage.removeItem(key);
      setConfirming(false);
      onOpenChange(false);
      onSuccess();
      return;
    }
    if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) onDiagLinkSessionExpired?.();
    if (action === 'reactivate') onSuccess();
    setError(result.kind === 'conflict' || result.kind === 'validation-error'
      ? result.message
      : `${action === 'cancel' ? 'Résiliation' : 'Réactivation'} non confirmée. Réessayez pour reprendre la même demande.`);
  };

  const content = confirming
    ? action === 'cancel'
      ? <><h3 className={styles.heading}>Confirmer la résiliation ?</h3><Text className={styles.explanation}>L’abonnement de « {machine?.name} » ne sera plus renouvelé. La machine restera accessible jusqu’à la fin de la période déjà payée.</Text></>
      : action === 'renew'
        ? <><h3 className={styles.heading}>Confirmer la réactivation du renouvellement ?</h3><Text className={styles.explanation}>La machine sera de nouveau incluse dans le prochain renouvellement.</Text></>
        : <><h3 className={styles.heading}>Confirmer la réactivation ?</h3><Text className={styles.explanation}>La machine sera réactivée immédiatement et le montant au prorata sera facturé pour la période restante jusqu’au prochain renouvellement.</Text></>
    : action === 'cancel'
      ? <><h3 className={styles.heading}>Résilier l’abonnement</h3><Text className={styles.explanation}>Cette machine ne sera plus renouvelée à la prochaine échéance. Les droits déjà payés resteront disponibles jusqu’à la fin de la période en cours.</Text></>
      : action === 'renew'
        ? <><h3 className={styles.heading}>Réactiver le renouvellement</h3><Text className={styles.explanation}>Cette machine sera de nouveau renouvelée à la prochaine échéance. Aucun montant supplémentaire ne sera facturé aujourd’hui.</Text></>
        : <><h3 className={styles.heading}>Réactiver l’abonnement</h3><Text className={styles.explanation}>L’abonnement sera réactivé immédiatement. Le montant jusqu’au prochain renouvellement sera calculé au prorata.</Text></>;
  const actionLabel = action === 'cancel' ? 'Résilier l’abonnement' : action === 'renew' ? 'Réactiver le renouvellement' : 'Réactiver l’abonnement';
  const confirmLabel = action === 'cancel' ? 'Confirmer la résiliation' : action === 'renew' ? 'Confirmer la réactivation' : 'Confirmer et réactiver';
  const busyLabel = action === 'cancel' ? 'Résiliation…' : 'Réactivation…';

  return <Dialog open={open} onOpenChange={(_event, data) => !data.open && close()}>
    <DialogSurface aria-labelledby="machine-subscription-dialog-title">
      <DialogBody>
        <DialogTitle id="machine-subscription-dialog-title" action={<DialogCloseButton disabled={busy} onClick={close} />}>Gérer la machine — {machine?.name}</DialogTitle>
        <DialogContent className={styles.content}>
          {content}
          {error&&<Text role="alert" className={styles.error}>{error}</Text>}
        </DialogContent>
        <DialogActions className={companyScoped?styles.companyActions:undefined} {...(companyScoped?{'data-company-machine-actions':true}:{})}>
          {confirming
            ? <><Button appearance="secondary" disabled={busy} onClick={()=>{setConfirming(false);setError('');}}>Annuler</Button><Button appearance="primary" className={action==='cancel'?styles.destructive:undefined} disabled={busy||!actionEnabled} onClick={()=>void changeSubscription()}>{busy?<><Spinner size="tiny"/> {busyLabel}</>:confirmLabel}</Button></>
            : <><Button appearance="secondary" disabled={busy} onClick={close}>Annuler</Button><Button appearance={action==='cancel'?'secondary':'primary'} className={action==='cancel'?styles.destructive:undefined} disabled={!actionEnabled} onClick={()=>setConfirming(true)}>{actionLabel}</Button></>}
        </DialogActions>
      </DialogBody>
    </DialogSurface>
  </Dialog>;
}
