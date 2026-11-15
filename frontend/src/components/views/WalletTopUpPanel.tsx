import {createPortal} from 'react-dom';
import styles from './CompanyFinancePanel.module.css';
import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Button, Field, Input } from '@fluentui/react-components';
import { getWalletTopUps, startWalletTopUp, type WalletOverview } from '../../services/walletTopUpService';
import type { GlobalFinanceIntervention } from './financeInterventions';
import { walletTopUpStatusLabel } from '../../utils/statusLabels';

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
type Pending = { id: string; amount: number };
export function WalletTopUpPanel({ companyId, getAccessToken, onDiagLinkSessionExpired, technicalTarget, repairTarget, onInterventionsChange, revision=0, onLoadComplete }: Props) {
  const storageKey = `diaglink:wallet-topup:${companyId}`;
  const [pending, setPending] = useState<Pending | null>(() => {
    try { const value = JSON.parse(localStorage.getItem(storageKey) || 'null');
      return value && typeof value.id === 'string' && typeof value.amount === 'number' ? value : null;
    } catch { return null; }
  });
  const [amount, setAmount] = useState(String(pending?.amount ?? 20));
  const [data, setData] = useState<WalletOverview | null>(null);
  const [message, setMessage] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [localRevision, setLocalRevision] = useState(0);
  const [historyOpen, setHistoryOpen] = useState(false);
  const historyId = useId();
  const lock = useRef(false);
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
    return () => { generation.current = current + 1; };
  }, [companyId, getAccessToken, onDiagLinkSessionExpired, revision, localRevision, onLoadComplete]);
  const numericAmount = Number(amount.replace(',', '.'));
  const validAmount = /^(?:\d+)(?:[.,]\d{1,2})?$/.test(amount) && numericAmount >= 10 && numericAmount <= 999999.99;
  async function submit() {
    if (lock.current || !data?.enabled || (!pending && !validAmount)) return;
    const operation = pending ?? { id: crypto.randomUUID(), amount: numericAmount };
    try { localStorage.setItem(storageKey, JSON.stringify(operation)); }
    catch { setMessage('Stockage local indisponible : impossible de sécuriser la reprise.'); return; }
    setPending(operation); setAmount(String(operation.amount));
    lock.current = true; setBusy(true); const current = generation.current;
    const result = await startWalletTopUp(getAccessToken, companyId, operation.id, operation.amount);
    lock.current = false;
    if (current !== generation.current) return;
    setBusy(false);
    if (result.kind === 'success') {
      try { localStorage.removeItem(storageKey); } catch { /* Rejouer la même clé reste idempotent. */ }
      setPending(null);
      const url = paymentUrl(result.data.paymentUrl);
      if (url) { window.location.assign(url); return; }
      setMessage(result.data.stage === 'Completed' ? 'Recharge déjà créditée.' : 'Lien de paiement indisponible. Actualisez puis réessayez.');
      setLocalRevision(value => value + 1);
      return;
    }
    setMessage(result.kind === 'conflict' || result.kind === 'validation-error' ? result.message : 'Paiement non confirmé. Réessayez la même recharge.');
    if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) onDiagLinkSessionExpired?.();
  }
  function paymentUrl(value: string | null) {
    try { const url = new URL(value || ''); return url.protocol === 'https:' && url.hostname === 'checkout.stripe.com' && !url.username && !url.password ? url.href : null; }
    catch { return null; }
  }
  const incompleteOperations = useMemo(
    () => data?.operations.filter(operation => operation.stage !== 'Completed') ?? [],
    [data]);
  const interventions = useMemo(
    () => incompleteOperations.map(operation => ({
      id: operation.id,
      type: 'wallet-top-up' as const,
      label: `Recharge de ${euro(operation.amountEur)} — ${walletTopUpStatusLabel(operation.stage)}`,
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
        <p><strong>{euro(operation.amountEur)}</strong> · {walletTopUpStatusLabel(operation.stage)}</p>
      </article>)}
    </section>
  );
  return <>
    <article className={styles.consumptionSummaryCard} aria-label="Recharge de crédit">
      <h4>Recharger le crédit</h4>
      {data && <>
        <div className={styles.metric}><strong>{euro(data.balance)}</strong><span> disponibles</span></div>
        <p className={styles.note}>1 € payé = 1 € de crédits. Crédit ajouté après confirmation du paiement.</p>
        <div className={styles.recharge}>
          <div className={styles.buttons}>{[10,20,50,100,200].map(value => <Button key={value} appearance={numericAmount===value?'primary':'secondary'} aria-pressed={numericAmount===value} disabled={busy || !!pending || !data.enabled} onClick={() => setAmount(String(value))}>{value} €</Button>)}</div>
          <Field className={styles.amountField} label="Montant libre en euros" hint="Minimum 10 €"><Input inputMode="decimal" value={amount} disabled={busy || !!pending || !data.enabled} onChange={(_, detail) => setAmount(detail.value)} /></Field>
          {!validAmount && <p>Minimum 10 €, au plus deux décimales.</p>}
          <Button appearance="primary" disabled={busy || loading || !data.enabled || !validAmount} onClick={() => void submit()}>{pending ? 'Réessayer la recharge' : 'Passer au paiement'}</Button>
          {!data.enabled && <p>Recharge indisponible : configuration Stripe requise.</p>}
        </div>
      </>}
      {loading&&<p className={styles.note}>Chargement du wallet…</p>}
      {message&&<p role="status">{message}</p>}
    </article>
    <article className={`${styles.consumptionSummaryCard} ${styles.topUpHistoryCard}`} aria-label="Historique des recharges de crédit">
      <h4>Historique des recharges de crédit</h4>
      {loading&&<p className={styles.note}>Chargement de l’historique…</p>}
      <button type="button" className={styles.machineToggle} aria-expanded={historyOpen} aria-controls={historyId} onClick={()=>setHistoryOpen(open=>!open)}>
        {historyOpen?'Masquer l’historique des recharges':'Voir l’historique des recharges'}
      </button>
    </article>
    {historyOpen&&<section id={historyId} className={styles.topUpHistoryDetail} aria-label="Détail de l’historique des recharges de crédit">
      {loading&&<p>Chargement de l’historique…</p>}
      {data?.operations.length===0&&<p className={styles.note}>Aucune recharge.</p>}
      {data?.operations.map(op => <article key={op.id} className={styles.rechargeHistory}>
        <strong>{euro(op.amountEur)}</strong><span className={`${styles.badge} ${op.stage==='Completed'?styles.topUpCompleted:op.stage==='AwaitingPayment'?styles.topUpAwaiting:''}`}>{walletTopUpStatusLabel(op.stage)}</span>
      </article>)}
    </section>}
    {technicalTarget ? createPortal(technicalDetails, technicalTarget) : technicalTarget === undefined ? technicalDetails : null}
    {repairTarget ? createPortal(repairTools, repairTarget) : repairTarget === undefined ? repairTools : null}
  </>;
}
