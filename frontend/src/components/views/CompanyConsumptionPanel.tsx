import {type ReactNode,useEffect,useState} from 'react';
import {getApiAuthHeaders} from '../../utils/apiAuth';
import {usagePeriods,usageWindow,type UsagePeriod} from '../../utils/aiUsage';
import styles from './CompanyFinancePanel.module.css';
import type {MachineFinanceIntervention} from './financeInterventions';
import {MachineSubscriptionDialog} from './MachineSubscriptionDialog';
import {MachineUserConsumption} from './MachineUserConsumption';
import {getMonthlyQuotaStatus} from './monthlyQuotaStatus';

type Metrics={responses:number;vision:number;summaries:number;input:number;output:number;tokens:number;unknown:number;unvalued:number;realCost:number;commercialCredit:number;walletRealCost:number;providers:{provider:string|null;model:string|null;count:number}[]};
type User={id:string|null;name:string;metrics:Metrics&{includedQuotaConsumed:number}};
type Machine={id:string|null;name:string;billable:boolean;budget:number;used:number;remaining:number;resetUtc:string|null;rightsEndUtc:string|null;hasPaidRights:boolean;metrics:Metrics;users:User[]};
type Report={companyName:string;walletBalance:number;metrics:Metrics;machines:Machine[]};
const euro=(n:number)=>new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(n);
const availability=(budget:number,used:number)=>budget>0?Math.max(0,Math.min(100,Math.round((budget-used)/budget*100))):0;
const quotaColor=(available:number)=>available===0?styles.quotaInactive:available>=50?styles.quotaHigh:available>=20?styles.quotaMedium:styles.quotaLow;
async function loadReport(companyId:string,token:()=>Promise<string|null>,filter:ReturnType<typeof usageWindow>,signal:AbortSignal){
 const {headers}=await getApiAuthHeaders(token);const query=new URLSearchParams({to:filter.to});if(filter.from)query.set('from',filter.from);
 const response=await fetch(`${import.meta.env.VITE_API_URL||'/api'}/companies/${companyId}/stripe/finance/consumption?${query}`,{headers,signal});
 if(!response.ok)throw new Error();return await response.json() as Report;
}

function ConsumptionSummary({budget,used,remaining,commercialCredit,progressLabel,showValues=true,billable,hasPaidRights=false,resetUtc,interventions=[],onExamineInterventions,children}:{budget:number;used:number;remaining:number;commercialCredit:number;progressLabel:string;showValues?:boolean;billable?:boolean;hasPaidRights?:boolean;resetUtc?:string|null;interventions?:MachineFinanceIntervention[];onExamineInterventions?:()=>void;children?:ReactNode}){
 const available=availability(budget,used);
 const quotaStatus=getMonthlyQuotaStatus(resetUtc,remaining);
 return <div className={`${styles.consumptionMetrics} ${styles.globalConsumptionMetrics}${billable===undefined?'':` ${styles.machineConsumptionMetrics}`}`}>
  {showValues&&<><article className={styles.consumptionSummaryCard}><h4>Quota inclus</h4>{billable===undefined&&<strong>{euro(budget)}</strong>}<div className={styles.globalQuotaBreakdown}><span>Consommé : <strong>{euro(used)}</strong></span><span>Restant : <strong>{euro(remaining)}</strong></span></div><progress className={`${styles.progress} ${quotaColor(available)}`} aria-label={progressLabel} value={available} max={100}/></article>
  <article className={styles.consumptionSummaryCard}><h4>Crédit supplémentaire consommé</h4><strong>{euro(commercialCredit)}</strong></article>
  {billable!==undefined&&<article className={styles.consumptionSummaryCard}><h4>État</h4>{billable&&hasPaidRights?<p className={styles.machineSubscriptionState}>Abonnement : <strong>Actif</strong></p>:!billable&&hasPaidRights?<><p className={styles.machineSubscriptionState}>Renouvellement : <strong>Arrêté</strong></p><p className={styles.machineSubscriptionState}>Accès : <strong>Actif</strong></p></>:<p className={styles.machineSubscriptionState}>Abonnement : <strong>Inactif</strong></p>}{quotaStatus&&<p className={styles.machineSubscriptionState}>Quota mensuel : <strong>{quotaStatus}</strong></p>}{interventions.length>0&&<div className={styles.machineInterventions}><span>Intervention requise</span><ul>{interventions.map(intervention=><li key={intervention.id}>{intervention.label}</li>)}</ul>{onExamineInterventions&&<button type="button" className={styles.machineToggle} onClick={onExamineInterventions}>Examiner / reprendre</button>}</div>}</article>}</>}
  {children}
 </div>;
}

export function CompanyConsumptionPanel({companyId,token,revision=0,view,onLoadComplete,globalContent,machineInterventions=[],onExamineInterventions,machineActionsEnabled=false,onMachineSubscriptionChanged,onDiagLinkSessionExpired}:{companyId:string;token:()=>Promise<string|null>;revision?:number;view:'global'|'machines';onLoadComplete?:()=>void;globalContent?:ReactNode;machineInterventions?:MachineFinanceIntervention[];onExamineInterventions?:()=>void;machineActionsEnabled?:boolean;onMachineSubscriptionChanged?:()=>void;onDiagLinkSessionExpired?:()=>void}){
 const [period,setPeriod]=useState<UsagePeriod>('month'),[filter,setFilter]=useState(()=>usageWindow('month'));
 const [openMachines,setOpenMachines]=useState<Set<string>>(()=>new Set());
 const [machineSearch,setMachineSearch]=useState(''),[showAllMachines,setShowAllMachines]=useState(false);
 const [managedMachine,setManagedMachine]=useState<Machine|null>(null);
 const [data,setData]=useState<Report|null>(null),[error,setError]=useState(false);
 useEffect(()=>{setOpenMachines(new Set());setMachineSearch('');setShowAllMachines(false);setManagedMachine(null);},[companyId]);
 useEffect(()=>{const c=new AbortController();setError(false);
  void(async()=>{try{const value=await loadReport(companyId,token,filter,c.signal);if(!c.signal.aborted)setData(value);
  }catch{if(!c.signal.aborted)setError(true);}finally{if(!c.signal.aborted)onLoadComplete?.();}})();return()=>c.abort();
 },[companyId,token,revision,filter,onLoadComplete]);
 const budget=data?.machines.reduce((sum,m)=>sum+m.budget,0)??0;
 const used=data?.machines.reduce((sum,m)=>sum+m.used,0)??0;
 const remaining=data?.machines.reduce((sum,m)=>sum+m.remaining,0)??0;
 const commercialCredit=data?.machines.reduce((sum,m)=>sum+m.metrics.commercialCredit,0)??0;
 const isGlobal=view==='global';
 const normalizedMachineSearch=machineSearch.trim().toLocaleLowerCase('fr-FR');
 const filteredMachines=data?.machines.filter(machine=>machine.name.toLocaleLowerCase('fr-FR').includes(normalizedMachineSearch))??[];
 const visibleMachines=normalizedMachineSearch||showAllMachines?filteredMachines:filteredMachines.slice(0,5);
 const toggleMachine=(key:string)=>setOpenMachines(current=>{const next=new Set(current);if(next.has(key))next.delete(key);else next.add(key);return next;});
 return <section aria-label={isGlobal?'Consommation globale':'Consommation par machine'} className={styles.panel}>
  <div className={`${styles.buttons} ${styles.consumptionPeriodControls} ${isGlobal?styles.globalConsumptionControls:styles.machineConsumptionControls}`}>
   <label>Période <select aria-label={isGlobal?'Période de consommation globale':'Période de consommation par machine'} value={period} onChange={event=>{const value=event.target.value as UsagePeriod;setPeriod(value);setFilter(usageWindow(value));}}>{Object.entries(usagePeriods).map(([value,label])=><option key={value} value={value}>{label}</option>)}</select></label>
   {!isGlobal&&<input className={styles.machineSearch} type="search" aria-label="Rechercher une machine" placeholder="Rechercher une machine..." value={machineSearch} onChange={event=>setMachineSearch(event.target.value)}/>}
  </div>
  {isGlobal?<>{error?<p role="alert">Suivi indisponible. Réessayez.</p>:!data?<p>Chargement…</p>:null}
   <ConsumptionSummary budget={budget} used={used} remaining={remaining} commercialCredit={commercialCredit} progressLabel="Quotas inclus disponibles" showValues={!!data}>{globalContent}</ConsumptionSummary></>:
   error?<p role="alert">Suivi indisponible. Réessayez.</p>:!data?<p>Chargement…</p>:
   <>{filteredMachines.length===0?<p className={styles.note}>Aucune machine trouvée.</p>:<div className={styles.machineCards}>{visibleMachines.map((machine,visualIndex)=>{
    const machineIndex=data.machines.indexOf(machine);
    const key=`${machine.id??'unassigned'}-${machineIndex}`,isOpen=openMachines.has(key),usersId=`machine-users-${machineIndex}`,hasAlternateBackground=visualIndex%2===0;
    const interventions=machine.id===null?[]:machineInterventions.filter(intervention=>intervention.machineId===machine.id);
    return <article className={`${styles.machineConsumptionCard}${hasAlternateBackground?` ${styles.machineConsumptionCardAlternate}`:''}`} data-machine-card={machine.name} key={key}>
     <header className={styles.machineConsumptionHeader}><div className={styles.machineTitle}><h3>{machine.name}</h3>{machine.id&&onMachineSubscriptionChanged&&<button type="button" className={styles.machineManage} onClick={()=>setManagedMachine(machine)}>Gérer</button>}</div><button type="button" className={styles.machineToggle} aria-expanded={isOpen} aria-controls={usersId} onClick={()=>toggleMachine(key)}>{isOpen?'Masquer la consommation par utilisateur':'Voir la consommation par utilisateur'}</button></header>
     <ConsumptionSummary budget={machine.budget} used={machine.used} remaining={machine.remaining} commercialCredit={machine.metrics.commercialCredit} progressLabel={`Quota inclus disponible — ${machine.name}`} billable={machine.billable} hasPaidRights={machine.hasPaidRights} resetUtc={machine.resetUtc} interventions={interventions} onExamineInterventions={onExamineInterventions}/>
     {isOpen&&<div id={usersId}><MachineUserConsumption machineName={machine.name} includedQuotaBudget={machine.budget} users={machine.users.map(user=>({id:user.id,name:user.name,includedQuotaConsumed:user.metrics.includedQuotaConsumed,commercialCredit:user.metrics.commercialCredit}))}/></div>}
    </article>;
   })}</div>}
   {!normalizedMachineSearch&&filteredMachines.length>5&&<button type="button" className={`${styles.machineToggle} ${styles.machineListToggle}`} aria-expanded={showAllMachines} onClick={()=>setShowAllMachines(showAll=>!showAll)}>{showAllMachines?'Voir moins de machines':'Voir plus de machines'}</button>}
   {onMachineSubscriptionChanged&&<MachineSubscriptionDialog open={managedMachine!==null} machine={managedMachine?.id?{id:managedMachine.id,name:managedMachine.name,billable:managedMachine.billable,hasPaidRights:managedMachine.hasPaidRights}:null} companyId={companyId} token={token} actionEnabled={machineActionsEnabled} onOpenChange={open=>{if(!open)setManagedMachine(null);}} onSuccess={onMachineSubscriptionChanged} onDiagLinkSessionExpired={onDiagLinkSessionExpired}/>}</>}
 </section>;
}
