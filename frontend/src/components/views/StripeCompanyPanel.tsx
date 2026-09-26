import {CompanySummaryBanner} from './CompanySummaryBanner';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Button } from '@fluentui/react-components';
import { stripeCompanyRequest, type StripeCompanySummary } from '../../services/stripeAdminService';
import { StripeMachineAdditionsPanel } from './StripeMachineAdditionsPanel';
import { WalletTopUpPanel } from './WalletTopUpPanel';
import { StripeMachineStatusPanel } from './StripeMachineStatusPanel';
import { AdminFinanceOverview } from './AdminFinanceOverview';
import { getAdminAccordionProps, type AdminAccordionControl } from './adminAccordion';
import styles from './CompanyFinancePanel.module.css';

interface Props {
  companyId: string;
  getAccessToken: () => Promise<string | null>;
  onDiagLinkSessionExpired?: () => void;
  accordion?: AdminAccordionControl;
}
const euro = (cents: number) => new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'EUR' }).format(cents / 100);
const subscriptionLabel = (status: string | null, cancelAtPeriodEnd?: boolean) => {
  const labels: Record<string, string> = {
    active: 'Actif', trialing: 'Période d’essai', past_due: 'Paiement en retard', unpaid: 'Impayé',
    canceled: 'Résilié', incomplete: 'Activation en attente', incomplete_expired: 'Activation expirée', paused: 'En pause'
  };
  const value = status ? labels[status] ?? status : 'Aucun abonnement enregistré';
  return cancelAtPeriodEnd ? `${value} · Résiliation prévue à échéance` : value;
};

export function StripeCompanyPanel({ companyId, getAccessToken, onDiagLinkSessionExpired, accordion }: Props) {
  const [technicalTarget, setTechnicalTarget] = useState<HTMLDivElement | null>(null);
  const [repairTarget, setRepairTarget] = useState<HTMLDivElement | null>(null);
  const [walletInterventions, setWalletInterventions] = useState<string[]>([]);
  const [machineInterventions, setMachineInterventions] = useState<string[]>([]);
  const [data, setData] = useState<StripeCompanySummary | null>(null);
  const [message, setMessage] = useState('');
  const [revision, setRevision] = useState(0);
  const repairDetails = useRef<HTMLDetailsElement>(null);
  const generation = useRef(0);
  const updateWalletInterventions = useCallback((values: string[]) => setWalletInterventions(values), []);
  const updateMachineInterventions = useCallback((values: string[]) => setMachineInterventions(values), []);
  useEffect(() => {
    const current = ++generation.current;
    setData(null); setMessage('Chargement des données enregistrées…');
    setWalletInterventions([]); setMachineInterventions([]);
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
  const interventions = [...walletInterventions, ...machineInterventions];
  const billableMachines = data?.machines?.filter(machine => machine.billable).length ?? data?.activeMachineCount ?? 0;
  const amountRemaining = data?.amountRemainingCents ?? 0;

  return <section aria-label="Finances et consommation">
    <CompanySummaryBanner key={companyId} companyId={companyId} account={data} token={getAccessToken} revision={revision}/>
    {data && <>
      <AdminFinanceOverview companyId={companyId} account={data} token={getAccessToken} revision={revision} accordion={accordion} machineContent={<WalletTopUpPanel key={companyId} companyId={companyId} getAccessToken={getAccessToken} onDiagLinkSessionExpired={onDiagLinkSessionExpired}
        technicalTarget={technicalTarget} repairTarget={repairTarget} onInterventionsChange={updateWalletInterventions} />}>
      <details className={`${styles.technical} ${styles.billingState}`} {...getAdminAccordionProps(accordion, 'billing')}>
      <summary><span className={styles.billingTitle}>État de facturation</span>
        <span className={styles.billingInlineState}>{subscriptionLabel(data.subscriptionStatus, data.cancelAtPeriodEnd)} · {data.activeMachineCount}/{billableMachines} machines · {amountRemaining > 0 ? `${euro(amountRemaining)} impayés` : 'Aucun impayé'} · <span className={interventions.length > 0 ? styles.interventionState : undefined}>{interventions.length > 0 ? `⚠ ${interventions.length} intervention${interventions.length > 1 ? 's' : ''}` : 'Aucune intervention'}</span></span>
      </summary>
      <div className={styles.billingBody}>
      <div className={styles.billingHeader}><p>Dernier état enregistré dans DiagLink.</p>
        <Button appearance="subtle" size="small" onClick={() => setRevision(value => value + 1)}>Actualiser les données enregistrées</Button></div>
      <dl className={styles.billingSummary}>
        <div><dt>Abonnement</dt><dd>{subscriptionLabel(data.subscriptionStatus, data.cancelAtPeriodEnd)}</dd></div>
        <div><dt>Machines actives / facturables</dt><dd>{data.activeMachineCount} / {billableMachines}</dd></div>
        <div><dt>Montant en attente</dt><dd>{amountRemaining > 0 ? euro(amountRemaining) : 'Aucun'}</dd></div>
        <div><dt>Opérations nécessitant une intervention</dt><dd>{interventions.length || 'Aucune'}</dd></div>
      </dl>
      {interventions.length > 0 && <div className={styles.billingAlert} role="alert"><strong>Intervention requise</strong>
        <ul>{interventions.map(value => <li key={value}>{value}</li>)}</ul>
        <Button onClick={() => {
          if (accordion) accordion.onSectionToggle('repairs', true);
          else if (repairDetails.current) repairDetails.current.open = true;
          requestAnimationFrame(() => repairTarget?.focus());
        }}>Examiner / reprendre</Button>
      </div>}
      <details className={`${styles.technical} ${styles.billingManagement}`}>
        <summary>Machines facturables ({billableMachines})</summary>
        <StripeMachineStatusPanel key={`status-${companyId}`} companyId={companyId} account={data} token={getAccessToken} refresh={() => setRevision(value => value + 1)} />
      </details>
      <StripeMachineAdditionsPanel companyId={companyId} getAccessToken={getAccessToken} account={data}
        onDiagLinkSessionExpired={onDiagLinkSessionExpired} technicalTarget={technicalTarget} repairTarget={repairTarget}
        onInterventionsChange={updateMachineInterventions} />
      </div>
      </details>
      <details className={styles.technical} {...getAdminAccordionProps(accordion, 'technical')}><summary>Détails techniques</summary>
      <section className={styles.diagnosticSubsection} aria-label="Données Stripe et abonnement enregistrées"><h4>Données Stripe et abonnement enregistrées</h4><dl className={styles.diagnosticSummary}>
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
      </section>
      <div ref={setTechnicalTarget}/>
      </details>
      <details ref={repairDetails} className={`${styles.technical}${interventions.length > 0 ? ` ${styles.repairTools}` : ''}`} {...getAdminAccordionProps(accordion, 'repairs')}><summary>Outils de réparation{interventions.length > 0 ? ` (${interventions.length})` : ''}</summary>
        {interventions.length === 0 && <p>Aucune opération ne nécessite d’intervention.</p>}
        <div ref={setRepairTarget} tabIndex={-1}/>
      </details>
      </AdminFinanceOverview>
    </>}
    <p role="status">{message}</p>
  </section>;
}
