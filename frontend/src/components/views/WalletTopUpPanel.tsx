import {createPortal} from 'react-dom';
import styles from './CompanyFinancePanel.module.css';
import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { getWalletTopUps, type WalletOverview } from '../../services/walletTopUpService';
import type { GlobalFinanceIntervention } from './financeInterventions';

interface Props {
  companyId: string;
  getAccessToken: () => Promise<string | null>;
  onDiagLinkSessionExpired?: () => void;
  technicalTarget?: HTMLElement | null;
  repairTarget?: HTMLElement | null;
  onInterventionsChange?: (interventions: GlobalFinanceIntervention[]) => void;
  revision?: number;
  onLoadComplete?: () => void;
}
const euro=(value:number)=>new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR',minimumFractionDigits:2,maximumFractionDigits:2}).format(value);
const topUpStatus=(value:string)=>value==='Completed'?'Payé':value==='AwaitingPayment'?'En attente de paiement':value==='PaymentCreated'?'Paiement créé':value;
export function WalletTopUpPanel({ companyId, getAccessToken, onDiagLinkSessionExpired, technicalTarget, repairTarget, onInterventionsChange, revision=0, onLoadComplete }: Props) {
  const [data, setData] = useState<WalletOverview | null>(null);
  const [message, setMessage] = useState('');
  const [loading, setLoading] = useState(true);
  const [historyOpen, setHistoryOpen] = useState(false);
  const historyId = useId();
  const generation = useRef(0);
  useEffect(() => {
    const current = ++generation.current; setLoading(true);
    getWalletTopUps(getAccessToken, companyId).then(result => {
      if (current !== generation.current) return;
      setLoading(false);
      if (result.kind === 'success') setData(result.data);
      else {
        setMessage(result.kind === 'forbidden' ? 'Accès Super Admin requis.' : 'Wallet indisponible.');
        if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) onDiagLinkSessionExpired?.();
      }
      onLoadComplete?.();
    });
    return () => { generation.current++; };
  }, [companyId, getAccessToken, onDiagLinkSessionExpired, revision, onLoadComplete]);
  const incompleteOperations = useMemo(
    () => data?.operations.filter(operation => operation.stage !== 'Completed') ?? [],
    [data]);
  const interventions = useMemo(
    () => incompleteOperations.map(operation => ({
      id: operation.id,
      type: 'wallet-top-up' as const,
      label: `Recharge de ${euro(operation.amountEur)} — ${topUpStatus(operation.stage)}`,
    })),
    [incompleteOperations]);
  useEffect(() => { onInterventionsChange?.(interventions); }, [interventions, onInterventionsChange]);
  const technicalDetails=(
      <section aria-label="Détails techniques des recharges"><h4>Recharges enregistrées</h4>
      {data?.operations.length === 0 && <p>Aucune recharge enregistrée.</p>}
      {data?.operations.map(op=><article key={op.id} className={styles.rechargeDiagnostic}>
          <p>Opération : {op.id}</p><p>Session : {op.stripeSessionId ?? '—'} · Paiement : {op.stripePaymentIntentId ?? '—'}</p>
          <p>Confirmé UTC : {op.paymentConfirmedAtUtc ?? '—'} · Ledger : {op.ledgerEntryId ?? '—'} · Événement : {op.externalEventId ?? '—'}</p>
      </article>)}
      </section>
  );
  const repairTools=(
    <section aria-label="Réparation des recharges"><h4>Recharges à reprendre</h4>
      {incompleteOperations.length === 0 && <p>Aucune recharge ne nécessite d’intervention.</p>}
      {incompleteOperations.map(operation => <article key={operation.id} className={styles.repairItem}>
        <p><strong>{euro(operation.amountEur)}</strong> · {topUpStatus(operation.stage)}</p>
      </article>)}
    </section>
  );
  return <>
    <article className={`${styles.consumptionSummaryCard} ${styles.topUpHistoryCard}`} aria-label="Historique des recharges de crédit">
      <h4>Historique des recharges de crédit</h4>
      {loading&&<p className={styles.note}>Chargement de l’historique…</p>}
      {message&&<p role="status">{message}</p>}
      <button type="button" className={styles.machineToggle} aria-expanded={historyOpen} aria-controls={historyId} onClick={()=>setHistoryOpen(open=>!open)}>
        {historyOpen?'Masquer l’historique des recharges':'Voir l’historique des recharges'}
      </button>
    </article>
    {historyOpen&&<section id={historyId} className={styles.topUpHistoryDetail} aria-label="Détail de l’historique des recharges de crédit">
      {loading&&<p>Chargement de l’historique…</p>}
      {data?.operations.length===0&&<p className={styles.note}>Aucune recharge.</p>}
      {data?.operations.map(op => <article key={op.id} className={styles.rechargeHistory}>
        <strong>{euro(op.amountEur)}</strong><span className={`${styles.badge} ${op.stage==='Completed'?styles.topUpCompleted:op.stage==='AwaitingPayment'?styles.topUpAwaiting:''}`}>{topUpStatus(op.stage)}</span>
      </article>)}
    </section>}
    {technicalTarget ? createPortal(technicalDetails, technicalTarget) : technicalTarget === undefined ? technicalDetails : null}
    {repairTarget ? createPortal(repairTools, repairTarget) : repairTarget === undefined ? repairTools : null}
  </>;
}
