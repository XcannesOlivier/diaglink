import {createPortal} from 'react-dom';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Button } from '@fluentui/react-components';
import { addStripeMachine, getStripeAdditions, type StripeAdditionSummary, type StripeCompanySummary } from '../../services/stripeAdminService';
import styles from './CompanyFinancePanel.module.css';

interface Props {
  companyId: string; account: StripeCompanySummary; getAccessToken: () => Promise<string | null>;
  onDiagLinkSessionExpired?: () => void;
  technicalTarget?: HTMLElement | null;
  repairTarget?: HTMLElement | null;
  onInterventionsChange?: (interventions: string[]) => void;
}
export function StripeMachineAdditionsPanel({ companyId, account, getAccessToken, onDiagLinkSessionExpired, technicalTarget, repairTarget, onInterventionsChange }: Props) {
  const [operations, setOperations] = useState<StripeAdditionSummary[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [loadError, setLoadError] = useState('');
  const inFlight = useRef(false);
  const generation = useRef(0);
  useEffect(() => {
    const current = ++generation.current;
    setLoaded(false); setLoadError('');
    getStripeAdditions(getAccessToken, companyId).then(result => {
      if (generation.current !== current) return;
      if (result.kind === 'success') { setOperations(result.data); setLoaded(true); }
      else {
        setLoadError(result.kind === 'forbidden' ? 'Accès Super Admin requis.' : 'Opérations Stripe indisponibles.');
        if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) onDiagLinkSessionExpired?.();
      }
    });
    return () => { generation.current++; };
  }, [companyId, getAccessToken, onDiagLinkSessionExpired, revision]);

  async function run(machineId: string) {
    if (inFlight.current || !account.testActionsEnabled || !machineId) return;
    const current = generation.current;
    inFlight.current = true; setBusy(true); setMessage('Traitement en cours…');
    const result = await addStripeMachine(getAccessToken, companyId, machineId);
    inFlight.current = false;
    if (current !== generation.current) return;
    setBusy(false);
    if (result.kind === 'success') setMessage(`Résultat : ${result.data.status}`);
    else {
      setMessage(result.kind === 'conflict' || result.kind === 'validation-error' ? result.message : 'Ajout non confirmé. Actualisez puis reprenez la même machine.');
      if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) onDiagLinkSessionExpired?.();
    }
    setRevision(r => r + 1);
  }
  const pendingOperations = useMemo(
    () => operations.filter(operation => operation.stage !== 'Completed' || operation.reconciliationRequired),
    [operations]);
  const interventions = useMemo(() => pendingOperations.map(operation =>
    `${operation.machineName} — ${operation.reconciliationRequired ? 'réconciliation requise' : 'ajout à reprendre'}`), [pendingOperations]);
  useEffect(() => { onInterventionsChange?.(interventions); }, [interventions, onInterventionsChange]);
  const technicalDetails = <section aria-label="Détails techniques des ajouts de machines">
    <div className={styles.cardHeading}><h4>Ajouts de machines enregistrés</h4>
      <Button appearance="subtle" size="small" disabled={busy} onClick={() => setRevision(value => value + 1)}>Actualiser les opérations</Button></div>
    {loadError && <p role="alert">{loadError}</p>}
    {!loaded && !loadError && <p>Chargement des opérations…</p>}
    {loaded && operations.length === 0 && <p>Aucune opération StripeMachineAddition.</p>}
    {operations.map(op => <article key={op.id} className={styles.rechargeDiagnostic}>
        <strong>{op.machineName} — {op.stage}</strong>
        {op.reconciliationRequired && <p role="alert">ReconciliationRequired : cycle terminé, vérification manuelle nécessaire.</p>}
        <dl>
          <dt>Opération / machine</dt><dd>{op.id} / {op.machineId}</dd>
          <dt>Facture Stripe</dt><dd>{op.stripeInvoiceId ?? '—'}</dd>
          <dt>Montants HT</dt><dd>IA : {op.aiAmountEur.toFixed(2)} € · service : {op.serviceAmountEur.toFixed(2)} € · total : {(op.aiAmountEur + op.serviceAmountEur).toFixed(2)} €</dd>
          <dt>Quantité cible</dt><dd>{op.targetQuantity}</dd>
          <dt>Activation UTC</dt><dd>{op.activatedAtUtc}</dd>
          <dt>Cycle UTC</dt><dd>{op.cycleStartUtc} → {op.cycleEndUtc}</dd>
          <dt>Paiement confirmé UTC</dt><dd>{op.paymentConfirmedAtUtc ?? '—'}</dd>
          <dt>Période machine</dt><dd>{op.machineBillingPeriodId ?? '—'}</dd>
          <dt>Événement Stripe</dt><dd>{op.externalEventId ?? '—'}</dd>
          <dt>Terminé UTC</dt><dd>{op.completedAtUtc ?? '—'}</dd>
        </dl>
      </article>)}
  </section>;
  const repairTools = <section aria-label="Réparation des ajouts de machines"><h4>Ajouts de machines à examiner</h4>
    {loaded && pendingOperations.length === 0 && <p>Aucun ajout de machine ne nécessite d’intervention.</p>}
    {pendingOperations.map(operation => <article key={operation.id} className={styles.repairItem}>
      <p><strong>{operation.machineName}</strong> · {operation.reconciliationRequired ? 'Réconciliation requise' : operation.stage}</p>
      {operation.reconciliationRequired
        ? <p>Le cycle est terminé. Vérifiez les données enregistrées et l’historique Stripe avant toute intervention.</p>
        : <Button disabled={busy || !account.testActionsEnabled} onClick={() => void run(operation.machineId)}>Reprendre l’opération</Button>}
    </article>)}
    <p role="status">{message}</p>
  </section>;
  return <>
    {technicalTarget ? createPortal(technicalDetails, technicalTarget) : technicalTarget === undefined ? technicalDetails : null}
    {repairTarget ? createPortal(repairTools, repairTarget) : repairTarget === undefined ? repairTools : null}
  </>;
}
