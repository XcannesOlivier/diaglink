import {useEffect,useState} from 'react';
import {financeRequest,type FinanceSummary} from '../../services/companyFinanceService';
import type {MachineDto} from '../../types/machine';
import styles from './CompanyFinancePanel.module.css';

export function CompanyMachineQuota({machine,token,revision,onSessionExpired}:{machine:MachineDto;token:()=>Promise<string|null>;revision:number;onSessionExpired?:()=>void}) {
 const [data,setData]=useState<FinanceSummary|null>(null),[error,setError]=useState(false);
 useEffect(()=>{let current=true;setData(null);setError(false);
  void financeRequest<FinanceSummary>(token,`?machineId=${encodeURIComponent(machine.id)}`).then(result=>{
   if(!current)return;
   if(result.kind==='success')setData(result.data);else{setError(true);if(result.kind==='unauthorized'&&result.diagLinkSessionExpired)onSessionExpired?.();}
  });return()=>{current=false;};
 },[machine.id,token,revision,onSessionExpired]);
 const activePeriod=!!data?.machineCreditResetUtc;
 const available=activePeriod?Math.max(0,Math.min(100,Math.round((data?.machineCreditRemaining??0)*10))):0;
 const quotaColor=available===0?styles.quotaInactive:available>=50?styles.quotaHigh:available>=20?styles.quotaMedium:styles.quotaLow;
 return <li className={styles.quotaRow} aria-label={`Quota ${machine.name}`}>
  <div className={styles.cardHeading}><strong>{machine.name}</strong><span className={machine.status==='active'?styles.badge:styles.warning}>{machine.status==='active'?'Active':'Non renouvelée'}</span></div>
  {error?<p role="alert">Quota indisponible. Actualisez pour réessayer.</p>:!data?<p>Chargement du quota…</p>:<>
   <div className={styles.barLabel}><strong>{available} % disponible</strong><span>{!activePeriod?(machine.status==='active'?'Quota mensuel non actif':'Aucune période active'):available===0?'Quota mensuel utilisé':'Quota mensuel'}</span></div>
   <progress className={`${styles.progress} ${quotaColor}`} aria-label={`Quota disponible de ${machine.name}`} value={available} max={100}/>
   {data.machineCreditResetUtc&&<p className={styles.note}>{machine.status==='active'?'Réinitialisation le ':'Accès maintenu jusqu’au '}<time dateTime={data.machineCreditResetUtc}>{new Date(data.machineCreditResetUtc).toLocaleDateString('fr-FR',{day:'numeric',month:'long',year:'numeric',timeZone:'UTC'})}</time></p>}
   {data.creditStatus==='AiCreditExhausted'&&<p className={styles.note}>Crédit IA épuisé. Ajoutez du crédit supplémentaire pour cette machine. Votre historique reste accessible.</p>}
  </>}
 </li>;
}
