import {useEffect,useState} from 'react';
import {getApiAuthHeaders} from '../../utils/apiAuth';
import type {StripeCompanySummary} from '../../services/stripeAdminService';
import styles from './CompanyFinancePanel.module.css';

const euro=(value:number)=>new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(value);
export function CompanySummaryBanner({companyId,account,token,revision}:{companyId:string;account:StripeCompanySummary|null;token:()=>Promise<string|null>;revision:number}) {
 const [wallet,setWallet]=useState('…');
 useEffect(()=>{
  const controller=new AbortController();setWallet('…');
  void (async()=>{try{
   const {headers}=await getApiAuthHeaders(token);
   const r=await fetch(`${import.meta.env.VITE_API_URL||'/api'}/companies/${companyId}/stripe/finance`,{headers,signal:controller.signal});
   if(!r.ok)throw new Error();const data=await r.json();if(!controller.signal.aborted)setWallet(euro(data.walletBalance));
  }catch{if(!controller.signal.aborted)setWallet('Indisponible');}})();
  return()=>controller.abort();
 },[companyId,token,revision]);
 const unpaid=account?.latestInvoiceStatus==='open'&&(account.amountRemainingCents??0)>0;
 const subscriptionLabel=!account?'Chargement…':account.subscriptionStatus==='past_due'||account.subscriptionStatus==='unpaid'?'Paiement en retard':
  account.subscriptionStatus==='active'?(unpaid?'Abonnement actif':'Abonnement payé'):
  account.subscriptionStatus==='trialing'&&account.machineRequestProvisioningCompleted?'Abonnement actif':
  ({trialing:'Abonnement en cours d’activation',canceled:'Résilié',incomplete:'Paiement initial en attente',incomplete_expired:'Paiement initial expiré',paused:'Suspendu'} as Record<string,string>)[account.subscriptionStatus??'']??(account.subscriptionStatus?'À vérifier':'Aucun abonnement');
 return <dl className={styles.companyBanner} aria-label="Résumé de l’entreprise">
  <div><dt>Machines facturables</dt><dd>{account?.activeMachineCount??'…'}</dd></div>
  <div><dt>Crédit supplémentaire</dt><dd>{wallet}</dd></div>
  <div><dt>Abonnement</dt><dd className={styles.bannerSubscription}>
   <span className={styles.bannerSubscriptionState}>{subscriptionLabel}</span>
   {unpaid&&<span className={styles.bannerUnpaid}><span>Montant impayé</span><strong>{euro(account!.amountRemainingCents!/100)}</strong></span>}
  </dd></div>
 </dl>;
}
