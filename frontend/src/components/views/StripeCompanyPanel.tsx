import {CompanySummaryBanner} from './CompanySummaryBanner';
import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { Button } from '@fluentui/react-components';
import { stripeCompanyRequest, type StripeCompanySummary } from '../../services/stripeAdminService';
import { StripeMachineAdditionsPanel } from './StripeMachineAdditionsPanel';
import { WalletTopUpPanel } from './WalletTopUpPanel';
import { AdminFinanceOverview } from './AdminFinanceOverview';
import { getAdminAccordionProps, type AdminAccordionControl } from './adminAccordion';
import type { GlobalFinanceIntervention, MachineFinanceIntervention } from './financeInterventions';
import styles from './CompanyFinancePanel.module.css';

interface Props {
  companyId: string;
  getAccessToken: () => Promise<string | null>;
  onDiagLinkSessionExpired?: () => void;
  accordion?: AdminAccordionControl;
  refreshRevision?: number;
  onRefreshComplete?: (revision:number) => void;
  onRefreshRequest?: () => void;
}
const euro = (cents: number) => new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'EUR' }).format(cents / 100);
const refreshSources=['company','summary','global','machines','wallet'] as const;
type RefreshSource=typeof refreshSources[number];

export function StripeCompanyPanel({ companyId, getAccessToken, onDiagLinkSessionExpired, accordion, refreshRevision=0, onRefreshComplete, onRefreshRequest }: Props) {
  const [technicalTarget, setTechnicalTarget] = useState<HTMLDivElement | null>(null);
  const [repairTarget, setRepairTarget] = useState<HTMLDivElement | null>(null);
  const [walletInterventions, setWalletInterventions] = useState<GlobalFinanceIntervention[]>([]);
  const [machineInterventions, setMachineInterventions] = useState<MachineFinanceIntervention[]>([]);
  const [globalInterventionsOpen, setGlobalInterventionsOpen] = useState(false);
  const [data, setData] = useState<StripeCompanySummary | null>(null);
  const [message, setMessage] = useState('');
  const repairDetails = useRef<HTMLDetailsElement>(null);
  const globalInterventionsId = useId();
  const generation = useRef(0);
  const dataRef = useRef<StripeCompanySummary|null>(null);
  const refreshState=useRef({revision:refreshRevision,pending:new Set<RefreshSource>(),reported:true});
  if(refreshState.current.revision!==refreshRevision){
    refreshState.current={revision:refreshRevision,pending:new Set(refreshSources),reported:refreshRevision===0};
  }
  dataRef.current=data;
  const finishRefresh=useCallback((source:RefreshSource)=>{
    const state=refreshState.current;
    if(state.revision!==refreshRevision||state.reported)return;
    state.pending.delete(source);
    if(state.pending.size===0){state.reported=true;onRefreshComplete?.(refreshRevision);}
  },[onRefreshComplete,refreshRevision]);
  const finishSummary=useCallback(()=>finishRefresh('summary'),[finishRefresh]);
  const finishConsumption=useCallback((view:'global'|'machines')=>finishRefresh(view),[finishRefresh]);
  const finishWallet=useCallback(()=>finishRefresh('wallet'),[finishRefresh]);
  const updateWalletInterventions = useCallback((values: GlobalFinanceIntervention[]) => setWalletInterventions(values), []);
  const updateMachineInterventions = useCallback((values: MachineFinanceIntervention[]) => setMachineInterventions(values), []);
  useEffect(() => {
    const current = ++generation.current;
    setMessage('Chargement des données enregistrées…');
    setWalletInterventions([]); setMachineInterventions([]); setGlobalInterventionsOpen(false);
    stripeCompanyRequest(getAccessToken, companyId).then(result => {
      if (generation.current !== current) return;
      if (result.kind === 'success') { setData(result.data); setMessage(''); }
      else {
        setMessage(result.kind === 'forbidden' ? 'Accès Super Admin requis.' : 'Impossible de charger Stripe.');
        if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) onDiagLinkSessionExpired?.();
        if(!dataRef.current){finishRefresh('global');finishRefresh('machines');finishRefresh('wallet');}
      }
      finishRefresh('company');
    });
    return () => { generation.current++; };
  }, [companyId, getAccessToken, onDiagLinkSessionExpired, refreshRevision, finishRefresh]);
  const interventions = [...walletInterventions, ...machineInterventions];
  const amountRemaining = data?.amountRemainingCents ?? 0;
  const examineInterventions = useCallback(() => {
    if (accordion) accordion.onSectionToggle('repairs', true);
    else if (repairDetails.current) repairDetails.current.open = true;
    requestAnimationFrame(() => repairTarget?.focus());
  }, [accordion, repairTarget]);

  return <section aria-label="Finances et consommation">
    <CompanySummaryBanner key={companyId} companyId={companyId} account={data} token={getAccessToken} revision={refreshRevision} onLoadComplete={finishSummary}/>
    {data && <>
      <AdminFinanceOverview companyId={companyId} token={getAccessToken} revision={refreshRevision} accordion={accordion}
        onConsumptionLoadComplete={finishConsumption} machineInterventions={machineInterventions} onExamineInterventions={examineInterventions}
        machineActionsEnabled={data.testActionsEnabled} onMachineSubscriptionChanged={onRefreshRequest} onDiagLinkSessionExpired={onDiagLinkSessionExpired} globalContent={<>
        <WalletTopUpPanel key={companyId} companyId={companyId} getAccessToken={getAccessToken} onDiagLinkSessionExpired={onDiagLinkSessionExpired}
          technicalTarget={technicalTarget} repairTarget={repairTarget} onInterventionsChange={updateWalletInterventions}
          revision={refreshRevision} onLoadComplete={finishWallet} />
        <article className={styles.consumptionSummaryCard} aria-label="Montant en attente"><h4>Montant en attente</h4><strong>{amountRemaining > 0 ? euro(amountRemaining) : 'Aucun'}</strong></article>
        <article className={`${styles.consumptionSummaryCard} ${styles.globalInterventionCard}`} aria-label="Opérations nécessitant une intervention"><h4>Opérations nécessitant une intervention</h4><strong>{interventions.length || 'Aucune'}</strong>
          {walletInterventions.length>0&&<button type="button" className={styles.machineToggle} aria-expanded={globalInterventionsOpen} aria-controls={globalInterventionsId} onClick={()=>setGlobalInterventionsOpen(open=>!open)}>{globalInterventionsOpen?'Masquer les interventions globales':'Voir les interventions globales'}</button>}
        </article>
        {globalInterventionsOpen&&walletInterventions.length>0&&<section id={globalInterventionsId} className={styles.topUpHistoryDetail} aria-label="Détail des interventions globales"><h4>Interventions globales</h4><ul>{walletInterventions.map(intervention=><li key={intervention.id}>{intervention.label}</li>)}</ul><Button onClick={examineInterventions}>Examiner / reprendre</Button></section>}
        </>} />
      <StripeMachineAdditionsPanel companyId={companyId} getAccessToken={getAccessToken} account={data}
        revision={refreshRevision}
        onDiagLinkSessionExpired={onDiagLinkSessionExpired} technicalTarget={technicalTarget} repairTarget={repairTarget}
        onInterventionsChange={updateMachineInterventions} />
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
    </>}
    <p role="status">{message}</p>
  </section>;
}
