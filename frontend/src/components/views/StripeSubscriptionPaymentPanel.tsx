import {useState} from 'react';
import {createPortal} from 'react-dom';
import {Button} from '@fluentui/react-components';
import {stripeSubscriptionPaymentRequest,type SubscriptionPaymentOverview} from '../../services/stripeAdminService';
import styles from './CompanyFinancePanel.module.css';

const euro=(cents:number)=>new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(cents/100);
const date=(value:string|null|undefined)=>value?new Date(value.endsWith('Z')?value:`${value}Z`).toLocaleDateString('fr-FR',{day:'numeric',month:'long',year:'numeric',timeZone:'UTC'}):'Non disponible';
const paymentStatus=(value:string|null|undefined)=>value==='paid'?'Payée':value==='open'?'En attente de paiement':value==='void'?'Annulée':value??'Non disponible';

export function StripeSubscriptionPaymentPanel({companyId,getAccessToken,onDiagLinkSessionExpired,diagnosticTarget}:{companyId:string;getAccessToken:()=>Promise<string|null>;onDiagLinkSessionExpired?:()=>void;diagnosticTarget?:HTMLElement|null}){
 const [data,setData]=useState<SubscriptionPaymentOverview|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
 async function refresh(){if(busy)return;setBusy(true);setError('');const result=await stripeSubscriptionPaymentRequest(getAccessToken,companyId);setBusy(false);if(result.kind==='success')setData(result.data);else{setData(null);setError('Paiement indisponible ou facture à vérifier.');if(result.kind==='unauthorized'&&result.diagLinkSessionExpired)onDiagLinkSessionExpired?.();}}
 let paymentUrl:string|null=null;try{const url=new URL(data?.invoice?.paymentUrl??'');if(data?.invoice?.status==='open'&&url.protocol==='https:'&&url.hostname==='invoice.stripe.com'&&!url.username&&!url.password)paymentUrl=url.href;}catch{/* No payable invoice. */}
 const diagnostic=data?<section aria-label="Diagnostic des paiements d’abonnement"><h4>Paiements d’abonnement — diagnostic</h4><p>Facture : {data.invoice?.id??'—'} · Motif : {data.invoice?.billingReason??'—'}</p>{data.payments.map(p=><p key={p.stripeInvoiceId}>{p.stripeInvoiceId} · {p.billingReason} · {p.status} · {p.periodStartUtc} → {p.periodEndUtc}</p>)}</section>:null;
 return <section aria-label="Paiement abonnement" className={styles.subscriptionPayment}>
  <div className={styles.cardHeading}><h4>Paiement de l’abonnement</h4><Button appearance="subtle" size="small" disabled={busy} onClick={()=>void refresh()}>Actualiser</Button></div>
  {error&&<p role="alert">{error}</p>}
  {data?.invoice?<><p><strong>{paymentStatus(data.invoice.status)}</strong> · Montant : {new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(data.invoice.quantity*29.9)} · {euro(data.invoice.amountPaidCents)} payé</p><p>Cycle : {date(data.invoice.startUtc)} → {date(data.invoice.endUtc)}</p>{paymentUrl&&<a href={paymentUrl} target="_self">Régler la facture</a>}<p className={styles.note}>Les quotas sont attribués uniquement après confirmation du paiement.</p></>:<p className={styles.note}>Actualisez pour consulter la dernière facture.</p>}
  {diagnosticTarget?createPortal(diagnostic,diagnosticTarget):diagnosticTarget===undefined?diagnostic:null}
 </section>;
}
