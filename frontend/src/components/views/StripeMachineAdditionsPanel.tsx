import { useEffect, useRef, useState } from 'react';
import { Button } from '@fluentui/react-components';
import { addStripeMachine, getStripeAdditions, type StripeAdditionSummary, type StripeCompanySummary } from '../../services/stripeAdminService';

interface Props {
  companyId: string; account: StripeCompanySummary; getAccessToken: () => Promise<string | null>;
  onDiagLinkSessionExpired?: () => void;
}
export function StripeMachineAdditionsPanel({ companyId, account, getAccessToken, onDiagLinkSessionExpired }: Props) {
  const [operations, setOperations] = useState<StripeAdditionSummary[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [revision, setRevision] = useState(0);
  const [machine, setMachine] = useState('');
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
  const candidates = (account.activeMachines ?? []).filter(m => !m.hasBillingPeriod && !operations.some(o => o.machineId === m.id && (o.stage !== 'Completed' || new Date(o.cycleEndUtc) > new Date())));
  const selected = candidates.some(m => m.id === machine) ? machine : candidates[0]?.id ?? '';
  const pending = operations.some(op => op.stage !== 'Completed');
  return <section aria-label="Ajouts de machines Stripe">
    <h4>Ajouts de machines</h4>
    <p>10 € IA complets + prorata du service à 19,90 €. Aucun budget IA avant paiement intégral confirmé.</p>
    <p>Pour payer la facture, utilisez le Dashboard Stripe. Le webhook reprend ensuite l’opération.</p>
    <Button disabled={busy} onClick={() => setRevision(r => r + 1)}>Actualiser les opérations</Button>
    {loadError && <p role="alert">{loadError}</p>}
    {!loaded && !loadError && <p>Chargement des opérations…</p>}
    {loaded && <>
      <label>Machine active à ajouter <select value={selected} onChange={e => setMachine(e.target.value)} disabled={busy || pending}>
        {!candidates.length && <option value="">Aucune machine éligible</option>}
        {candidates.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}
      </select></label>
      <Button disabled={busy || !account.testActionsEnabled || account.subscriptionStatus !== 'active' || !selected || pending}
        onClick={() => void run(selected)}>Lancer l’ajout Stripe</Button>
      {pending && <p>Une opération est en cours : terminez-la avant un autre ajout.</p>}
      {operations.length === 0 && <p>Aucune opération StripeMachineAddition.</p>}
      {operations.map(op => <article key={op.id} style={{ borderTop: '1px solid #ccc', marginTop: 12, paddingTop: 8, overflowWrap: 'anywhere' }}>
        <strong>{op.machineName} — {op.stage}</strong>
        {op.reconciliationRequired && <p role="alert">ReconciliationRequired : cycle terminé, vérification manuelle nécessaire.</p>}
        <details><summary>Voir les détails</summary><dl>
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
        </dl></details>
        <Button disabled={busy || !account.testActionsEnabled || op.reconciliationRequired || op.stage === 'Completed'}
          onClick={() => void run(op.machineId)}>{op.stage === 'Completed' ? 'Rejouer sans effet' : 'Reprendre l’opération'}</Button>
      </article>)}
    </>}
    <p role="status">{message}</p>
  </section>;
}
