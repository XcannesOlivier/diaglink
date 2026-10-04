import {useEffect,useState} from 'react';
import {getApiAuthHeaders} from '../../utils/apiAuth';
import type {StripeCompanySummary} from '../../services/stripeAdminService';
import styles from './CompanyFinancePanel.module.css';

const euro=(value:number)=>new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(value);
export function CompanySummaryBanner({companyId,account,token,revision,onLoadComplete}:{companyId:string;account:StripeCompanySummary|null;token:()=>Promise<string|null>;revision:number;onLoadComplete?:()=>void}) {
 const [wallet,setWallet]=useState('…');
 useEffect(()=>{
  const controller=new AbortController();
  void (async()=>{try{
   const {headers}=await getApiAuthHeaders(token);
   const r=await fetch(`${import.meta.env.VITE_API_URL||'/api'}/companies/${companyId}/stripe/finance`,{headers,signal:controller.signal});
   if(!r.ok)throw new Error();const data=await r.json();if(!controller.signal.aborted)setWallet(euro(data.walletBalance));
  }catch{if(!controller.signal.aborted)setWallet(current=>current==='…'?'Indisponible':current);}finally{if(!controller.signal.aborted)onLoadComplete?.();}})();
  return()=>controller.abort();
 },[companyId,token,revision,onLoadComplete]);
 const machineCount=account?(account.activeMachineCount??0):null;
 const activeSubscriptionCount=account?(account.activeMachineCount??0):null;
 return <dl className={styles.companyBanner} aria-label="Résumé de l’entreprise">
    <div><dt>Machines</dt><dd>{machineCount===null?'…':`${machineCount} machine${machineCount>1?'s':''}`}</dd></div>
    <div><dt>Crédit supplémentaire</dt><dd>Crédit supplémentaire : {wallet}</dd></div>
    <div><dt>Abonnements actifs</dt><dd>{activeSubscriptionCount===null?'…':`${activeSubscriptionCount} abonnement${activeSubscriptionCount>1?'s':''} actif${activeSubscriptionCount>1?'s':''}`}</dd></div>
 </dl>;
}
