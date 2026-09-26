import {createPortal} from 'react-dom';
import styles from './CompanyFinancePanel.module.css';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Button, Field, Input } from '@fluentui/react-components';
import { getWalletTopUps, startWalletTopUp, type WalletOverview } from '../../services/walletTopUpService';

interface Props {
  companyId: string;
  getAccessToken: () => Promise<string | null>;
  onDiagLinkSessionExpired?: () => void;
  technicalTarget?: HTMLElement | null;
  repairTarget?: HTMLElement | null;
  onInterventionsChange?: (interventions: string[]) => void;
}
const euro=(value:number)=>new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR',minimumFractionDigits:2,maximumFractionDigits:2}).format(value);
type Pending = { id: string; amount: number };
const topUpStatus=(value:string)=>value==='Completed'?'Payé':value==='AwaitingPayment'?'En attente de paiement':value==='PaymentCreated'?'Paiement créé':value;
export function WalletTopUpPanel({ companyId, getAccessToken, onDiagLinkSessionExpired, technicalTarget, repairTarget, onInterventionsChange }: Props) {
  const storageKey = `diaglink:wallet-topup:${companyId}`;
  const [pending, setPending] = useState<Pending | null>(() => {
    try { const value = JSON.parse(localStorage.getItem(storageKey) || 'null');
      return value && typeof value.id === 'string' && typeof value.amount === 'number' ? value : null;
    } catch { return null; }
  });
  const [amount, setAmount] = useState(String(pending?.amount ?? 10));
  const [data, setData] = useState<WalletOverview | null>(null);
  const [message, setMessage] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);
  const lock = useRef(false); const generation = useRef(0);
  useEffect(() => {
    const current = ++generation.current; setLoading(true);
    getWalletTopUps(getAccessToken, companyId).then(result => {
      if (current !== generation.current) return;
      setLoading(false);
      if (result.kind === 'success') setData(result.data);
      else {
        setData(null); setMessage(result.kind === 'forbidden' ? 'Accès Super Admin requis.' : 'Wallet indisponible.');
        if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) onDiagLinkSessionExpired?.();
      }
    });
    return () => { generation.current++; };
  }, [companyId, getAccessToken, onDiagLinkSessionExpired, revision]);
  const valid = /^(?:\d+)(?:[.,]\d{1,2})?$/.test(amount) && Number(amount.replace(',', '.')) >= 10 && Number(amount.replace(',', '.')) <= 999999.99;
  async function submit(replay?: Pending) {
    if (lock.current || !data?.enabled || (!replay && !pending && !valid)) return;
    const operation = replay ?? pending ?? { id: crypto.randomUUID(), amount: Number(amount.replace(',', '.')) };
    // Persist before sending: an uncertain network response must keep the same operation ID across reloads.
    try { localStorage.setItem(storageKey, JSON.stringify(operation)); }
    catch { setMessage('Stockage local indisponible : impossible de sécuriser la reprise.'); return; }
    setPending(operation); setAmount(String(operation.amount));
    lock.current = true; setBusy(true); const current = generation.current;
    const result = await startWalletTopUp(getAccessToken, companyId, operation.id, operation.amount);
    lock.current = false;
    if (current !== generation.current) return;
    setBusy(false);
    if (result.kind === 'success') {
      setMessage(`Recharge : ${result.data.status}`);
      // SQL now owns the operation; subsequent retries are available in the operation list.
      try { localStorage.removeItem(storageKey); } catch { /* Retaining the key is safe: replay remains idempotent. */ }
      setPending(null);
    } else {
      setMessage(result.kind === 'conflict' || result.kind === 'validation-error' ? result.message : 'Paiement non confirmé. Réessayez la même recharge.');
      if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) onDiagLinkSessionExpired?.();
    }
    setRevision(r => r + 1);
  }
  function paymentUrl(value: string | null) {
    try { const url = new URL(value || ''); return url.protocol === 'https:' && url.hostname === 'checkout.stripe.com' && !url.username && !url.password ? url.href : null; }
    catch { return null; }
  }
  const incompleteOperations = useMemo(
    () => data?.operations.filter(operation => operation.stage !== 'Completed') ?? [],
    [data]);
  const interventions = useMemo(() => {
    const values = incompleteOperations.map(operation => `Recharge de ${euro(operation.amountEur)} — ${topUpStatus(operation.stage)}`);
    if (pending && !incompleteOperations.some(operation => operation.id === pending.id)) values.push(`Recharge de ${euro(pending.amount)} — réponse à confirmer`);
    return values;
  }, [incompleteOperations, pending]);
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
      {incompleteOperations.length === 0 && !pending && <p>Aucune recharge ne nécessite d’intervention.</p>}
      {pending && !incompleteOperations.some(operation => operation.id === pending.id) && <article className={styles.repairItem}>
        <p><strong>Réponse à confirmer</strong> · {euro(pending.amount)}</p>
        <Button disabled={busy || loading || !data?.enabled} onClick={() => void submit(pending)}>Réessayer la recharge</Button>
      </article>}
      {incompleteOperations.map(operation => <article key={operation.id} className={styles.repairItem}>
        <p><strong>{euro(operation.amountEur)}</strong> · {topUpStatus(operation.stage)}</p>
        <Button disabled={busy || loading || !data?.enabled || (!!pending && pending.id !== operation.id)}
          onClick={() => void submit({ id: operation.id, amount: operation.amountEur })}>Reprendre la recharge</Button>
      </article>)}
    </section>
  );
  return <section className={styles.recharge} aria-label="Recharge wallet">
    <div className={styles.cardHeading}><h4>Crédit supplémentaire</h4>
    <Button appearance="subtle" size="small" disabled={busy || loading} onClick={() => setRevision(r => r + 1)}>Actualiser le wallet</Button></div>
    {loading && <p>Chargement du wallet…</p>}
    {data && <div className={styles.walletTopUpLayout}>
      <div className={styles.walletTopUpForm}>
      <div className={styles.metric}><strong>{euro(data.balance)}</strong><span> disponibles</span></div>
      {!data.walletExists && <p>Le wallet sera créé après le premier paiement confirmé.</p>}
      <p className={styles.note}>1 € payé = 1 € de crédits. Crédit uniquement après confirmation du paiement.</p>
      {!data.enabled && <p>Recharge indisponible : configuration de paiement requise.</p>}
      <div className={styles.walletTopUpControls}>
        <div className={styles.buttons}>{[10, 20, 50, 100, 200].map(value => <Button key={value} appearance={Number(amount.replace(',','.'))===value?'primary':'secondary'} aria-pressed={Number(amount.replace(',','.'))===value} disabled={busy || !!pending || !data.enabled} onClick={() => setAmount(String(value))}>{value} €</Button>)}</div>
        <Field className={styles.amountField} label="Montant libre en euros" hint="Minimum 10 €"><Input inputMode="decimal" value={amount} disabled={busy || !!pending}
          onChange={(_,d) => setAmount(d.value)} /></Field>
        {!valid && <p>Minimum 10 €, au plus deux décimales (maximum technique : 999 999,99 €).</p>}
        <Button appearance="primary" disabled={busy || loading || !data.enabled || !valid} onClick={() => void submit()}>{pending ? 'Réessayer la recharge' : 'Passer au paiement'}</Button>
      </div>
      </div>
      <div className={styles.walletTopUpHistory}>
      <details className={styles.technical}><summary>Historique des recharges</summary>
      {data.operations.length===0&&<p className={styles.note}>Aucune recharge.</p>}
      {data.operations.map(op => {
        const url = op.stage === 'AwaitingPayment' || op.stage === 'PaymentCreated' ? paymentUrl(op.paymentUrl) : null;
        return <article key={op.id} className={styles.rechargeHistory}>
          <strong>{euro(op.amountEur)}</strong><span className={`${styles.badge} ${op.stage==='Completed'?styles.topUpCompleted:op.stage==='AwaitingPayment'?styles.topUpAwaiting:''}`}>{topUpStatus(op.stage)}</span>
          {url && <a className={styles.topUpLink} href={url} target="_self">Payer maintenant</a>}

        </article>;
      })}
      </details>
      </div>
    </div>}
    {technicalTarget ? createPortal(technicalDetails, technicalTarget) : technicalTarget === undefined ? technicalDetails : null}
    {repairTarget ? createPortal(repairTools, repairTarget) : repairTarget === undefined ? repairTools : null}
    <p role="status">{message}</p>
  </section>;
}
