import {CompanySummaryBanner} from './CompanySummaryBanner';
import { useEffect, useRef, useState } from 'react';
import { Button } from '@fluentui/react-components';
import { stripeCompanyRequest, type StripeCompanySummary } from '../../services/stripeAdminService';
import { StripeMachineAdditionsPanel } from './StripeMachineAdditionsPanel';
import { WalletTopUpPanel } from './WalletTopUpPanel';
import { StripeMachineStatusPanel } from './StripeMachineStatusPanel';
import { AdminFinanceOverview } from './AdminFinanceOverview';
import styles from './CompanyFinancePanel.module.css';

interface Props {
  companyId: string;
  getAccessToken: () => Promise<string | null>;
  onDiagLinkSessionExpired?: () => void;
}

export function StripeCompanyPanel({ companyId, getAccessToken, onDiagLinkSessionExpired }: Props) {
  const [diagnosticTarget,setDiagnosticTarget]=useState<HTMLDivElement|null>(null);
  const [costsTarget,setCostsTarget]=useState<HTMLDivElement|null>(null);
  const [data, setData] = useState<StripeCompanySummary | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [revision, setRevision] = useState(0);
  const inFlight = useRef(false);
  const generation = useRef(0);
  useEffect(() => {
    const current = ++generation.current;
    setData(null); setMessage('Chargement Stripe…');
    stripeCompanyRequest(getAccessToken, companyId).then(result => {
      if (generation.current !== current) return;
      if (result.kind === 'success') { setData(result.data); setMessage(''); }
      else {
        setMessage(result.kind === 'forbidden' ? 'Accès Super Admin requis.' : 'Impossible de charger Stripe.');
        if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) onDiagLinkSessionExpired?.();
      }
    });
    return () => { generation.current++; };
  }, [companyId, getAccessToken, onDiagLinkSessionExpired, revision]);

  async function run(action: 'customer' | 'subscription') {
    if (inFlight.current || !data?.testActionsEnabled) return;
    const current = generation.current;
    inFlight.current = true; setBusy(true); setMessage('Opération en cours…');
    const result = await stripeCompanyRequest(getAccessToken, companyId, action);
    inFlight.current = false;
    if (generation.current !== current) return;
    setBusy(false);
    if (result.kind === 'success') { setData(result.data); setMessage('Opération terminée.'); }
    else {
      setMessage(result.kind === 'conflict' || result.kind === 'validation-error' ? result.message
        : result.kind === 'forbidden' ? 'Accès Super Admin requis.' : 'Opération non confirmée. Vous pouvez réessayer.');
      if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) onDiagLinkSessionExpired?.();
    }
  }

  return <section aria-label="Finances et consommation">
    <CompanySummaryBanner key={companyId} companyId={companyId} account={data} token={getAccessToken} revision={revision}/>
    {data && <>
      <AdminFinanceOverview companyId={companyId} account={data} token={getAccessToken} revision={revision} diagnosticTarget={diagnosticTarget} costsTarget={costsTarget} machineContent={<><WalletTopUpPanel key={companyId} companyId={companyId} getAccessToken={getAccessToken} onDiagLinkSessionExpired={onDiagLinkSessionExpired} diagnosticTarget={diagnosticTarget} diagnosticContent={<>
      <section className={styles.diagnosticSubsection} aria-label="Stripe et abonnement"><h4>Stripe et abonnement</h4><dl className={styles.diagnosticSummary}>
        <dt>BillingAccount</dt><dd>{data.billingAccountId ?? '—'}</dd>
        <dt>StripeCustomerId</dt><dd>{data.stripeCustomerId ?? '—'}</dd>
        <dt>StripeSubscriptionId</dt><dd>{data.stripeSubscriptionId ?? '—'}</dd>
        <dt>SubscriptionStatus</dt><dd>{data.subscriptionStatus ?? '—'}</dd>
        <dt>Résiliation à échéance</dt><dd>{data.cancelAtPeriodEnd?'Oui':'Non'}</dd>
        <dt>Dernière facture</dt><dd>{data.latestInvoiceId ?? '—'} · {data.latestInvoiceStatus ?? '—'}</dd>
        <dt>Montant restant dû</dt><dd>{((data.amountRemainingCents ?? 0)/100).toFixed(2)} EUR</dd>
        <dt>CurrentPeriodStartUtc</dt><dd>{data.currentPeriodStartUtc ?? '—'}</dd>
        <dt>CurrentPeriodEndUtc</dt><dd>{data.currentPeriodEndUtc ?? '—'}</dd>
        <dt>Machines actives</dt><dd>{data.activeMachineCount}</dd>
      </dl>
      {!data.testActionsEnabled && <p>Actions indisponibles : Stripe doit être configuré en mode test.</p>}
      <Button disabled={busy || !data.testActionsEnabled} onClick={() => void run('customer')}>Créer / récupérer le client Stripe</Button>
      <Button disabled={busy || !data.testActionsEnabled || data.activeMachineCount === 0} onClick={() => void run('subscription')}>Créer / récupérer l’abonnement Stripe</Button>
      </section><section className={styles.diagnosticSubsection} aria-label="Machines facturables"><h4>Machines facturables</h4>
      <StripeMachineAdditionsPanel companyId={companyId} getAccessToken={getAccessToken}
        onDiagLinkSessionExpired={onDiagLinkSessionExpired} account={data} />
      <StripeMachineStatusPanel key={`status-${companyId}`} companyId={companyId} account={data} token={getAccessToken} refresh={()=>setRevision(r=>r+1)} />
      </section></>}/></>}>
      <div className={styles.diagnosticAdvanced}><h4>Détails techniques avancés</h4><div ref={setDiagnosticTarget}/></div>
      <div className={styles.diagnosticCosts}><h4>Coûts IA</h4><div ref={setCostsTarget}/></div>
      <Button appearance="subtle" size="small" disabled={busy} onClick={() => setRevision(r => r + 1)}>Actualiser le compte Stripe</Button>
      </AdminFinanceOverview>
    </>}
    <p role="status">{message}</p>
  </section>;
}
