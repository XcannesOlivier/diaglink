import {useEffect,useId,useState} from 'react';
import {ChevronDown20Regular,ChevronUp20Regular} from '@fluentui/react-icons';
import {useMediaQuery} from '../../hooks/useThemeProvider';
import {financeRequest,type FinanceSummary,type CompanyConsumptionMachine} from '../../services/companyFinanceService';
import type {MachineDto} from '../../types/machine';
import styles from './CompanyFinancePanel.module.css';
import {MachineUserConsumption} from './MachineUserConsumption';
import {getMonthlyQuotaStatus} from './monthlyQuotaStatus';

const euro=(value:number)=>new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(value);

export function CompanyMachineQuota({machine,consumption,alternate=false,usersOpen=false,usersId,onToggleUsers,onManage,token,revision,onSessionExpired}:{machine:MachineDto;consumption?:CompanyConsumptionMachine;alternate?:boolean;usersOpen?:boolean;usersId:string;onToggleUsers:()=>void;onManage?:()=>void;token:()=>Promise<string|null>;revision:number;onSessionExpired?:()=>void}) {
 const [data,setData]=useState<FinanceSummary|null>(null),[error,setError]=useState(false);
 const [stateOpen,setStateOpen]=useState(false),stateContentId=useId(),isMobile=useMediaQuery('(max-width: 767px)');
 useEffect(()=>{let current=true;setData(null);setError(false);
  void financeRequest<FinanceSummary>(token,`?machineId=${encodeURIComponent(machine.id)}`).then(result=>{
   if(!current)return;
   if(result.kind==='success')setData(result.data);else{setError(true);if(result.kind==='unauthorized'&&result.diagLinkSessionExpired)onSessionExpired?.();}
  });return()=>{current=false;};
 },[machine.id,token,revision,onSessionExpired]);
 const activePeriod=!!data?.machineCreditResetUtc;
 const quotaStatus=getMonthlyQuotaStatus(data?.machineCreditResetUtc,data?.machineCreditRemaining);
 const available=activePeriod?Math.max(0,Math.min(100,Math.round((data?.machineCreditRemaining??0)*10))):0;
 const quotaColor=available===0?styles.quotaInactive:available>=50?styles.quotaHigh:available>=20?styles.quotaMedium:styles.quotaLow;
 return <li className={`${styles.machineConsumptionCard}${alternate?` ${styles.machineConsumptionCardAlternate}`:''}`} data-machine-card={machine.name} aria-label={`Quota ${machine.name}`}>
  <header className={styles.machineConsumptionHeader}><div className={styles.machineTitle}><h4>{machine.name}</h4>{onManage&&<button type="button" className={styles.machineManage} onClick={onManage}>Gérer</button>}</div><button type="button" className={styles.machineToggle} aria-expanded={usersOpen} aria-controls={usersId} onClick={onToggleUsers}>{usersOpen?'Masquer la consommation par utilisateur':'Voir la consommation par utilisateur'}</button></header>
  {error?<p role="alert">Quota indisponible. Actualisez pour réessayer.</p>:!data?<p>Chargement du quota…</p>:<>
   <div className={`${styles.consumptionMetrics} ${styles.companyQuotaMetrics}`}>
    <article className={styles.companyQuotaMetricCard} aria-label={`Quota inclus de ${machine.name}`}>
     <h4>Quota inclus</h4>
     <div className={styles.barLabel}><strong>{available} % disponible</strong></div>
     <progress className={`${styles.progress} ${quotaColor}`} aria-label={`Quota disponible de ${machine.name}`} value={available} max={100}/>
     {data.creditStatus==='AiCreditExhausted'&&<p className={styles.note}>Crédit IA épuisé. Ajoutez du crédit supplémentaire pour cette machine. Votre historique reste accessible.</p>}
    </article>
    <article className={styles.companyQuotaMetricCard} aria-label={`Crédit supplémentaire consommé de ${machine.name}`}>
     <h4>Crédit supplémentaire consommé</h4>
     <strong>{consumption?euro(consumption.commercialCredit):'—'}</strong>
    </article>
    <article className={`${styles.companyQuotaMetricCard} ${styles.companyQuotaStateCard}`} aria-label={`État de ${machine.name}`}>
     {isMobile?<button type="button" className={styles.companyQuotaStateToggle} aria-expanded={stateOpen} aria-controls={stateContentId} onClick={()=>setStateOpen(open=>!open)}><span>État</span>{stateOpen?<ChevronUp20Regular aria-hidden="true"/>:<ChevronDown20Regular aria-hidden="true"/>}</button>:<h4>État</h4>}
     {(!isMobile||stateOpen)&&<div id={stateContentId} className={styles.companyQuotaStateContent}>
      {!consumption?<p className={styles.machineSubscriptionState}>Chargement…</p>:consumption.billable&&consumption.hasPaidRights?<p className={styles.machineSubscriptionState}>Abonnement : <strong>Actif</strong></p>:!consumption.billable&&consumption.hasPaidRights?<><p className={styles.machineSubscriptionState}>Renouvellement : <strong>Arrêté</strong></p><p className={styles.machineSubscriptionState}>Accès : <strong>Actif</strong></p></>:<p className={styles.machineSubscriptionState}>Abonnement : <strong>Inactif</strong></p>}
      {quotaStatus&&<p className={styles.machineSubscriptionState}>Quota mensuel : <strong>{quotaStatus}</strong></p>}
     </div>}
    </article>
   </div>
   {usersOpen&&<div id={usersId}>{consumption?<MachineUserConsumption machineName={machine.name} includedQuotaBudget={consumption.includedQuotaBudget} users={consumption.users} showIncludedQuotaConsumed={false}/>:<p>Chargement de la consommation…</p>}</div>}
  </>}
 </li>;
}
