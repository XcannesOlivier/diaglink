import {CompanyMachineQuota} from './CompanyMachineQuota';
import type {MachineDto} from '../../types/machine';
import {useEffect,useId,useRef,useState} from 'react';
import {Button,Field,Input} from '@fluentui/react-components';
import {financeRequest,type FinanceSummary,type ClientTopUp} from '../../services/companyFinanceService';
import styles from './CompanyFinancePanel.module.css';
import {getMachines} from '../../services/machineService';

const euro=(n:number)=>new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR',maximumFractionDigits:2}).format(n);
const labels:Record<string,string>={active:'Actif',past_due:'Paiement en retard',unpaid:'Impayé',canceled:'Résilié',incomplete:'Paiement initial en attente',incomplete_expired:'Paiement initial expiré',trialing:'Abonnement en cours d’activation',paused:'Suspendu'};
type Pending={requestId:string;amount:number;currency:'EUR'};
export function CompanyFinancePanel({companyId,getAccessToken,onDiagLinkSessionExpired}:{companyId:string;machine?:{id:string;name:string};getAccessToken:()=>Promise<string|null>;onDiagLinkSessionExpired?:()=>void}) {
  const [data,setData]=useState<FinanceSummary|null>(null);
  const [machines,setMachines]=useState<MachineDto[]|null>(null);
  const [machinesError,setMachinesError]=useState(false);
  const [expandedMachines,setExpandedMachines]=useState(false);
  const machinesListId=useId();
  useEffect(()=>setExpandedMachines(false),[companyId]);
  const machineCount=machines?.filter(m=>m.status==='active').length??null;
  const [message,setMessage]=useState(''),[amount,setAmount]=useState('20'),[busy,setBusy]=useState(false),[revision,setRevision]=useState(0);
  useEffect(()=>{let active=true;setMachines(null);setMachinesError(false);getMachines(getAccessToken).then(result=>{
    if(!active)return;
    if(result.kind==='success')setMachines(result.data.filter(m=>m.companyId===companyId));
    else {setMachinesError(true);if(result.kind==='unauthorized'&&result.diagLinkSessionExpired)onDiagLinkSessionExpired?.();}
  });return()=>{active=false;};},[companyId,getAccessToken,revision,onDiagLinkSessionExpired]);
  const lock=useRef(false),generation=useRef(0);
  async function payInvoice(){
    if(lock.current)return;
    lock.current=true;setBusy(true);const current=generation.current;
    const result=await financeRequest<{paymentUrl:string}>(getAccessToken,'/invoice-payment',{});
    lock.current=false;setBusy(false);if(current!==generation.current)return;
    if(result.kind==='success'){
      try{const url=new URL(result.data.paymentUrl);if(url.protocol==='https:'&&url.hostname==='invoice.stripe.com'&&!url.username&&!url.password){window.location.assign(url.href);return;}}catch{/* Fail closed. */}
    }
    setMessage('Facture indisponible. Actualisez le compte ou réessayez plus tard.');
    if(result.kind==='unauthorized'&&result.diagLinkSessionExpired)onDiagLinkSessionExpired?.();
  }
  const key=`diaglink:client-topup:${companyId}`;
  const [pending,setPending]=useState<Pending|null>(()=>{try{const p=JSON.parse(localStorage.getItem(key)||'null');return p&&typeof p.requestId==='string'&&typeof p.amount==='number'&&p.currency==='EUR'?p:null;}catch{return null;}});
  useEffect(()=>{
    const current=++generation.current;setData(null);setMessage('');
    Promise.all([financeRequest<FinanceSummary>(getAccessToken,''),financeRequest<ClientTopUp[]>(getAccessToken,'/topups')]).then(([summary,topups])=>{
      if(current!==generation.current)return;
      if(summary.kind==='success')setData(summary.data);else setMessage('Informations financières indisponibles.');
      if(topups.kind!=='success')setMessage('Impossible de charger les recharges. Actualisez avant de réessayer.');
      for(const result of [summary,topups])if(result.kind==='unauthorized'&&result.diagLinkSessionExpired)onDiagLinkSessionExpired?.();
    });return()=>{generation.current++;};
  },[companyId,getAccessToken,onDiagLinkSessionExpired,revision]);
  async function recharge(){
    if(lock.current||!data?.rechargeEnabled)return;
    const value=Number(amount.replace(',','.'));
    if(!pending&&(!/^\d+(?:[.,]\d{1,2})?$/.test(amount)||value<10||value>999999.99)){setMessage('Saisissez au moins 10 €, avec deux décimales au maximum.');return;}
    const op:Pending=pending??{requestId:crypto.randomUUID(),amount:value,currency:'EUR'};
    try{localStorage.setItem(key,JSON.stringify(op));}catch{setMessage('Stockage local indisponible. Réessayez plus tard.');return;}
    setPending(op);lock.current=true;setBusy(true);const current=generation.current;
    const result=await financeRequest<ClientTopUp>(getAccessToken,'/topups',op);
    lock.current=false;setBusy(false);if(current!==generation.current)return;
    if(result.kind==='success'){
      try{localStorage.removeItem(key);}catch{/* Same key is safe on retry. */}
      setPending(null);setMessage('');
      const url=safeUrl(result.data.paymentUrl);
      if(url){window.location.assign(url);return;}
      if(result.data.status==='Completed'){setRevision(v=>v+1);return;}
      setMessage('Paiement indisponible. Veuillez réessayer.');
    }else{setMessage('Recharge non confirmée. Réessayez la même demande.');if(result.kind==='unauthorized'&&result.diagLinkSessionExpired)onDiagLinkSessionExpired?.();}
  }
  function safeUrl(value:string|null){try{const u=new URL(value||'');return u.protocol==='https:'&&u.hostname==='checkout.stripe.com'&&!u.username&&!u.password?u.href:null;}catch{return null;}}
  return <section aria-label="Finances de l’entreprise" className={styles.panel}>
    <div className={styles.heading}><p className={styles.intro}>Gardez un œil sur vos crédits et votre abonnement.</p><Button disabled={busy} onClick={()=>setRevision(v=>v+1)}>Actualiser</Button></div>
    {!data&&!message&&<p>Chargement…</p>}
    {data&&<>
      {data.creditStatus==='AiCreditExhausted'&&<div role="alert" className={styles.alert}>Crédit IA épuisé. Ajoutez du crédit supplémentaire pour continuer. Votre historique reste accessible.</div>}
      <div className={`${styles.cards} ${styles.companyCards}`}>
        <article className={styles.card} aria-label="Machines et quotas"><h3>Machines et quotas</h3>
          {machinesError?<p role="alert">Machines indisponibles. Actualisez pour réessayer.</p>:machines===null?<p>Chargement des machines…</p>:machines.length===0?<p>Aucune machine dans votre entreprise.</p>:<ul id={machinesListId} className={styles.quotaList}>{(expandedMachines?machines:machines.slice(0,5)).map(m=><CompanyMachineQuota key={m.id} machine={m} token={getAccessToken} revision={revision} onSessionExpired={onDiagLinkSessionExpired}/>)}</ul>}
          {machines&&machines.length>5&&<Button appearance="secondary" style={{marginTop:16,width:'100%'}} aria-expanded={expandedMachines} aria-controls={machinesListId} onClick={()=>setExpandedMachines(value=>!value)}>{expandedMachines?'Voir moins':'Voir plus de machines'}</Button>}
        </article>
        <article className={styles.card} aria-label="Crédit supplémentaire"><h3>Crédit supplémentaire</h3><p className={styles.muted}>Partagé entre toutes vos machines.</p>
          <div className={styles.metric}><strong>{euro(data.walletBalance)}</strong><span> disponibles</span></div>
          <div className={styles.walletBar} aria-hidden="true"><span style={{width:data.walletBalance>0?'100%':'0%'}}/></div>
          <p className={styles.note}>Utilisé automatiquement quand le quota inclus de la machine est épuisé.</p>
          <div className={styles.recharge}>
            <h4>Ajouter du crédit</h4>
            <div className={styles.buttons}>{[10,20,50,100,200].map(n=><Button key={n} appearance={Number((pending?String(pending.amount):amount).replace(',','.'))===n?'primary':'secondary'} aria-pressed={Number((pending?String(pending.amount):amount).replace(',','.'))===n} disabled={busy||!!pending||!data.rechargeEnabled} onClick={()=>setAmount(String(n))}>{n} €</Button>)}</div>
            <Field className={styles.amountField} label="Montant libre en euros" hint="Minimum 10 €"><Input value={pending?String(pending.amount):amount} disabled={busy||!!pending||!data.rechargeEnabled} onChange={(_,d)=>setAmount(d.value)} inputMode="decimal"/></Field>
            <Button appearance="primary" disabled={busy||!data.rechargeEnabled} onClick={()=>void recharge()}>{pending?'Réessayer la recharge':'Ajouter du crédit supplémentaire'}</Button>
            {!data.rechargeEnabled&&<p>La recharge est temporairement indisponible.</p>}
          </div>
        </article>
        <details className={`${styles.card} ${styles.subscription}`} aria-label="Abonnement">
        <summary className={`${styles.cardHeading} ${styles.subscriptionSummary}`}><h3>Abonnement</h3><span className={data.subscriptionStatus==='past_due'||data.subscriptionStatus==='unpaid'?styles.warning:styles.badge}>{data.subscriptionStatus?(labels[data.subscriptionStatus]??'À vérifier'):'Aucun abonnement'}</span></summary>
        <dl className={styles.details}>
        <dt>Prochaine échéance</dt><dd>{data.nextDueUtc&&!data.cancelAtPeriodEnd&&data.subscriptionStatus!=='canceled'?new Date(data.nextDueUtc).toLocaleString('fr-FR'):'—'}</dd>
        <dt>Nombre total de machines</dt><dd>{machines?.length??'Indisponible'}</dd>
        <dt>Machines facturées</dt><dd>{machineCount??'Indisponible'}</dd>
        <dt>Montant mensuel estimé</dt><dd>{machineCount===null?'Indisponible':`${euro(machineCount*29.90)} HT / mois`}</dd>
        </dl>
        {data.cancelAtPeriodEnd&&<p className={styles.warning}>Résiliation prévue à l’échéance</p>}
        <p className={styles.note}>Estimation pour les machines actives à 29,90 € HT par mois, hors recharges et ajustements.</p>
        {data.unpaidInvoiceAmount!==null&&<div className={styles.alert}><strong>Facture impayée · {euro(data.unpaidInvoiceAmount)}</strong><p>Montant à régler. Une recharge de crédit ne règle pas cette facture.</p><Button appearance="primary" disabled={busy} onClick={()=>void payInvoice()}>Régler la facture</Button></div>}
        </details>
      </div>
    </>}
    <p role="status">{message}</p>
  </section>;
}
