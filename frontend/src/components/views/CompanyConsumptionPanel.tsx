import {Fragment,type ReactNode,useEffect,useRef,useState} from 'react';
import {getApiAuthHeaders} from '../../utils/apiAuth';
import {usagePeriods,usageWindow,type UsagePeriod} from '../../utils/aiUsage';
import styles from './CompanyFinancePanel.module.css';
import type {MachineFinanceIntervention} from './financeInterventions';
import {MachineSubscriptionDialog} from './MachineSubscriptionDialog';
import {MachineUserConsumption} from './MachineUserConsumption';
import {getMonthlyQuotaStatus} from './monthlyQuotaStatus';

type Metrics={responses:number;vision:number;summaries:number;input:number;output:number;tokens:number;cacheReadInput?:number;cacheCreationInput?:number;cacheCreation5mInput?:number;cacheCreation1hInput?:number;unknown:number;unvalued:number;realCost:number;commercialCredit:number;walletRealCost:number;providers:{provider:string|null;model:string|null;count:number}[]};
type User={id:string|null;name:string;metrics:Metrics&{includedQuotaConsumed:number}};
type Machine={id:string|null;name:string;billable:boolean;budget:number;used:number;remaining:number;resetUtc:string|null;rightsEndUtc:string|null;hasPaidRights:boolean;metrics:Metrics;users:User[]};
type Report={companyName:string;walletBalance:number;metrics:Metrics;machines:Machine[]};
type TokenHistoryCall={callNumber:number;inputTokens:number;outputTokens:number;totalTokens:number;cacheReadInputTokens?:number;cacheCreationInputTokens?:number;cacheCreation5mInputTokens?:number;cacheCreation1hInputTokens?:number;model:string;stopReason:string;tools:string[]};
type TokenHistoryItem={createdAtUtc:string;inputTokens:number;outputTokens:number;totalTokens:number;cacheReadInputTokens?:number;cacheCreationInputTokens?:number;cacheCreation5mInputTokens?:number;cacheCreation1hInputTokens?:number;model:string|null;provider:string|null;calls:TokenHistoryCall[]|null};
type TokenHistoryResponse={items:TokenHistoryItem[];hasMore:boolean};
type TokenHistoryState={open:boolean;loading:boolean;error:boolean;items:TokenHistoryItem[];hasMore:boolean};
const euro=(n:number)=>new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(n);
const tokenCount=new Intl.NumberFormat('fr-FR');
const tokenHistoryPageSize=5;
const tokenDate=new Intl.DateTimeFormat('fr-FR',{day:'2-digit',month:'2-digit',year:'numeric',hour:'2-digit',minute:'2-digit',second:'2-digit',hourCycle:'h23'});
const formatTokenDate=(value:string)=>tokenDate.format(new Date(/(?:Z|[+-]\d{2}:?\d{2})$/i.test(value)?value:`${value}Z`));
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
 const [tokenHistories,setTokenHistories]=useState<Record<string,TokenHistoryState>>({});
 const [openTokenCallDetails,setOpenTokenCallDetails]=useState<Set<string>>(()=>new Set());
 const [data,setData]=useState<Report|null>(null),[error,setError]=useState(false);
 const companyIdRef=useRef(companyId);companyIdRef.current=companyId;
 useEffect(()=>{setOpenMachines(new Set());setMachineSearch('');setShowAllMachines(false);setManagedMachine(null);setTokenHistories({});setOpenTokenCallDetails(new Set());},[companyId]);
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
 const toggleMachine=(key:string,machineId:string|null)=>{
  setOpenMachines(current=>{const next=new Set(current);if(next.has(key))next.delete(key);else next.add(key);return next;});
  if(machineId)setTokenHistories(current=>{const history=current[machineId];return history?{...current,[machineId]:{...history,open:false}}:current;});
 };
 const loadTokenHistory=async(machineId:string,skip:number)=>{
  setTokenHistories(current=>({...current,[machineId]:{...(current[machineId]??{open:true,items:[],hasMore:false}),loading:true,error:false}}));
  try{
   const {headers}=await getApiAuthHeaders(token);
   const response=await fetch(`${import.meta.env.VITE_API_URL||'/api'}/companies/${companyId}/stripe/machines/${machineId}/token-history?skip=${skip}&take=${tokenHistoryPageSize}`,{headers});
   if(!response.ok)throw new Error();
   const value=await response.json() as TokenHistoryResponse;
   if(companyIdRef.current!==companyId)return;
   setTokenHistories(current=>{const history=current[machineId];if(!history)return current;return {...current,[machineId]:{...history,loading:false,error:false,items:skip===0?value.items:[...history.items,...value.items],hasMore:value.hasMore}};});
  }catch{
   if(companyIdRef.current!==companyId)return;
   setTokenHistories(current=>{const history=current[machineId];if(!history)return current;return {...current,[machineId]:{...history,loading:false,error:true}};});
  }
 };
 const toggleTokenHistory=(machineId:string,machineKey:string)=>{
  const history=tokenHistories[machineId];
  setOpenMachines(current=>{const next=new Set(current);next.delete(machineKey);return next;});
  if(history){setTokenHistories(current=>({...current,[machineId]:{...current[machineId],open:!current[machineId].open}}));return;}
  void loadTokenHistory(machineId,0);
 };
 const toggleTokenCallDetails=(key:string)=>setOpenTokenCallDetails(current=>current.has(key)?new Set():new Set([key]));
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
    const history=machine.id?tokenHistories[machine.id]:undefined,historyId=`machine-token-history-${machineIndex}`;
    const interventions=machine.id===null?[]:machineInterventions.filter(intervention=>intervention.machineId===machine.id);
    return <article className={`${styles.machineConsumptionCard}${hasAlternateBackground?` ${styles.machineConsumptionCardAlternate}`:''}`} data-machine-card={machine.name} key={key}>
     <header className={styles.machineConsumptionHeader}><div className={styles.machineTitle}><h3>{machine.name}</h3>{machine.id&&onMachineSubscriptionChanged&&<button type="button" className={styles.machineManage} onClick={()=>setManagedMachine(machine)}>Gérer</button>}</div><button type="button" className={styles.machineToggle} aria-expanded={isOpen} aria-controls={usersId} onClick={()=>toggleMachine(key,machine.id)}><span className={styles.desktopButtonLabel}>{isOpen?'Masquer la consommation par utilisateur':'Voir la consommation par utilisateur'}</span><span className={styles.mobileButtonLabel}>Par utilisateur</span></button>{machine.id&&<button type="button" className={styles.machineToggle} aria-expanded={history?.open??false} aria-controls={historyId} onClick={()=>toggleTokenHistory(machine.id!,key)}><span className={styles.desktopButtonLabel}>{history?.open?'Masquer l’historique des tokens':'Historique des tokens'}</span><span className={styles.mobileButtonLabel}>Par tokens</span></button>}</header>
     <ConsumptionSummary budget={machine.budget} used={machine.used} remaining={machine.remaining} commercialCredit={machine.metrics.commercialCredit} progressLabel={`Quota inclus disponible — ${machine.name}`} billable={machine.billable} hasPaidRights={machine.hasPaidRights} resetUtc={machine.resetUtc} interventions={interventions} onExamineInterventions={onExamineInterventions}/>
     {history?.open&&<div id={historyId} className={styles.machineUsers}>{history.loading&&history.items.length===0?<p>Chargement de l’historique…</p>:history.items.length===0&&!history.error?<p>Aucun appel enregistré pour cette machine.</p>:history.items.length>0?<div className={`${styles.compactTableScroll} ${styles.tokenHistoryScroll}`}><table className={`${styles.compactTable} ${styles.tokenHistoryTable}`}><thead><tr>{['Date / heure','Input','Cache lu','Cache créé','Cache 5 min','Cache 1 h','Output','Total','Modèle','Détails'].map(label=><th scope="col" key={label}>{label}</th>)}</tr></thead><tbody>{history.items.map((item,index)=>{
      const detailKey=`${machine.id}-${item.createdAtUtc}-${index}`,detailsOpen=openTokenCallDetails.has(detailKey),detailId=`machine-token-call-details-${machineIndex}-${index}`;
      return <Fragment key={`${item.createdAtUtc}-${index}`}><tr><td>{formatTokenDate(item.createdAtUtc)}</td><td>{tokenCount.format(item.inputTokens)}</td><td>{tokenCount.format(item.cacheReadInputTokens??0)}</td><td>{tokenCount.format(item.cacheCreationInputTokens??0)}</td><td>{tokenCount.format(item.cacheCreation5mInputTokens??0)}</td><td>{tokenCount.format(item.cacheCreation1hInputTokens??0)}</td><td>{tokenCount.format(item.outputTokens)}</td><td><strong>{tokenCount.format(item.totalTokens)}</strong></td><td>{item.model??'—'}</td><td>{item.calls&&item.calls.length>0?<button type="button" className={`${styles.machineToggle} ${styles.tokenHistoryDetailToggle}`} aria-expanded={detailsOpen} aria-controls={detailId} onClick={()=>toggleTokenCallDetails(detailKey)}>{detailsOpen?'Masquer':'Détails'}</button>:<span className={styles.note}>—</span>}</td></tr>{detailsOpen&&item.calls&&<tr id={detailId} className={styles.tokenCallDetailRow}><td colSpan={10}><div className={styles.tokenCallDetail}><strong>Détail des appels Claude</strong><div className={`${styles.compactTableScroll} ${styles.tokenHistoryScroll}`}><table className={`${styles.compactTable} ${styles.tokenHistoryTable}`}><thead><tr>{['Appel','Input','Cache lu','Cache créé','Cache 5 min','Cache 1 h','Output','Total','Arrêt','Outils'].map(label=><th scope="col" key={label}>{label}</th>)}</tr></thead><tbody>{item.calls.map(call=><tr key={call.callNumber}><td>{tokenCount.format(call.callNumber)}</td><td>{tokenCount.format(call.inputTokens)}</td><td>{tokenCount.format(call.cacheReadInputTokens??0)}</td><td>{tokenCount.format(call.cacheCreationInputTokens??0)}</td><td>{tokenCount.format(call.cacheCreation5mInputTokens??0)}</td><td>{tokenCount.format(call.cacheCreation1hInputTokens??0)}</td><td>{tokenCount.format(call.outputTokens)}</td><td><strong>{tokenCount.format(call.totalTokens)}</strong></td><td>{call.stopReason}</td><td>{call.tools.length>0?call.tools.join(', '):'—'}</td></tr>)}</tbody></table></div></div></td></tr>}</Fragment>;
     })}</tbody></table></div>:null}{history.error&&<p role="alert">Historique des tokens indisponible.</p>}{history.items.length>0&&history.hasMore&&<button type="button" className={styles.machineToggle} disabled={history.loading} onClick={()=>void loadTokenHistory(machine.id!,history.items.length)}>{history.loading?'Chargement…':'Afficher plus'}</button>}</div>}
     {isOpen&&<div id={usersId}><MachineUserConsumption machineName={machine.name} includedQuotaBudget={machine.budget} users={machine.users.map(user=>({id:user.id,name:user.name,includedQuotaConsumed:user.metrics.includedQuotaConsumed,commercialCredit:user.metrics.commercialCredit}))}/></div>}
    </article>;
   })}</div>}
   {!normalizedMachineSearch&&filteredMachines.length>5&&<button type="button" className={`${styles.machineToggle} ${styles.machineListToggle}`} aria-expanded={showAllMachines} onClick={()=>setShowAllMachines(showAll=>!showAll)}>{showAllMachines?'Voir moins de machines':'Voir plus de machines'}</button>}
   {onMachineSubscriptionChanged&&<MachineSubscriptionDialog open={managedMachine!==null} machine={managedMachine?.id?{id:managedMachine.id,name:managedMachine.name,billable:managedMachine.billable,hasPaidRights:managedMachine.hasPaidRights}:null} companyId={companyId} token={token} actionEnabled={machineActionsEnabled} onOpenChange={open=>{if(!open)setManagedMachine(null);}} onSuccess={onMachineSubscriptionChanged} onDiagLinkSessionExpired={onDiagLinkSessionExpired}/>}</>}
 </section>;
}
