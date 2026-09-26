import {useEffect,useState} from 'react';
import {getApiAuthHeaders} from '../../utils/apiAuth';
import {usagePeriods,usageWindow,type UsagePeriod} from '../../utils/aiUsage';
import styles from './CompanyFinancePanel.module.css';

type Metrics={responses:number;vision:number;summaries:number;input:number;output:number;tokens:number;unknown:number;unvalued:number;realCost:number;commercialCredit:number;walletRealCost:number;providers:{provider:string|null;model:string|null;count:number}[]};
type User={id:string|null;name:string;metrics:Metrics};
type Machine={id:string|null;name:string;billable:boolean;budget:number;used:number;remaining:number;resetUtc:string|null;rightsEndUtc:string|null;hasPaidRights:boolean;metrics:Metrics;users:User[]};
type Report={companyName:string;walletBalance:number;metrics:Metrics;machines:Machine[]};
const euro=(n:number)=>new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(n);
const date=(s:string|null)=>s?new Date(s.endsWith('Z')?s:`${s}Z`).toLocaleDateString('fr-FR',{day:'numeric',month:'long',year:'numeric',timeZone:'UTC'}):'Non disponible';
const status=(m:Machine)=>`${m.billable?'Active · facturable':'Non renouvelée'}${!m.resetUtc?' · Quota mensuel non actif':''}`;
const cost=(m:Metrics)=>euro(m.realCost);
async function loadReport(companyId:string,token:()=>Promise<string|null>,filter:ReturnType<typeof usageWindow>,signal:AbortSignal){
 const {headers}=await getApiAuthHeaders(token);const query=new URLSearchParams({to:filter.to});if(filter.from)query.set('from',filter.from);
 const response=await fetch(`${import.meta.env.VITE_API_URL||'/api'}/companies/${companyId}/stripe/finance/consumption?${query}`,{headers,signal});
 if(!response.ok)throw new Error();return await response.json() as Report;
}

export function CompanyConsumptionPanel({companyId,token,revision=0}:{companyId:string;token:()=>Promise<string|null>;revision?:number}){
 const [period,setPeriod]=useState<UsagePeriod>('month'),[filter,setFilter]=useState(()=>usageWindow('month'));
 const [machineId,setMachineId]=useState<string|null|undefined>(),[userId,setUserId]=useState<string|null|undefined>();
 const [data,setData]=useState<Report|null>(null),[error,setError]=useState(false);
 useEffect(()=>{setMachineId(undefined);setUserId(undefined);},[companyId]);
 useEffect(()=>{const c=new AbortController();setData(null);setError(false);
  void(async()=>{try{const value=await loadReport(companyId,token,filter,c.signal);if(!c.signal.aborted)setData(value);
  }catch{if(!c.signal.aborted)setError(true);}})();return()=>c.abort();
 },[companyId,token,revision,filter]);
 const machine=machineId===undefined?undefined:data?.machines.find(m=>m.id===machineId);
 const user=userId===undefined?undefined:machine?.users.find(u=>u.id===userId);
 const metrics=user?.metrics??machine?.metrics??data?.metrics;
 const scoped=machineId!==undefined;
 const used=machine?.used??data?.machines.reduce((sum,m)=>sum+m.used,0)??0;
 const budget=machine?.budget??data?.machines.reduce((sum,m)=>sum+m.budget,0)??0;
 const remaining=machine?.remaining??data?.machines.reduce((sum,m)=>sum+m.remaining,0)??0;
 const commercialCredit=machine?.metrics.commercialCredit??data?.machines.reduce((sum,m)=>sum+m.metrics.commercialCredit,0)??0;
 const available=budget>0?Math.max(0,Math.min(100,Math.round((budget-used)/budget*100))):0;
 const color=available===0?styles.quotaInactive:available>=50?styles.quotaHigh:available>=20?styles.quotaMedium:styles.quotaLow;
 return <section aria-label="Suivi de consommation" className={styles.panel}>
  <div className={styles.buttons}>
   <label>Période <select aria-label="Période de consommation" value={period} onChange={event=>{const value=event.target.value as UsagePeriod;setPeriod(value);setFilter(usageWindow(value));}}>{Object.entries(usagePeriods).map(([value,label])=><option key={value} value={value}>{label}</option>)}</select></label>
   <button type="button" className={styles.machineToggle} onClick={()=>setFilter(usageWindow(period))}>Actualiser</button>
  </div>
  <p className={styles.muted}>Tous les usages</p>
  {error?<p role="alert">Suivi indisponible. Réessayez.</p>:!data?<p>Chargement…</p>:<>
   {scoped&&<nav aria-label="Fil d’Ariane consommation" className={styles.consumptionBreadcrumb}>
    <span className={styles.breadcrumbCompany}>{data.companyName}</span>
    {machine&&<><span className={styles.breadcrumbSeparator} aria-hidden="true">{' > '}</span><strong className={styles.breadcrumbMachine}>{machine.name}</strong></>}
    {user&&<><span className={styles.breadcrumbSeparator} aria-hidden="true">{' > '}</span><span className={styles.breadcrumbUser}>{user.name}</span></>}
   </nav>}
   {scoped&&<button className={styles.machineToggle} onClick={()=>{setMachineId(undefined);setUserId(undefined);}}>Retour aux machines</button>}
   {userId!==undefined&&<button className={styles.machineToggle} onClick={()=>setUserId(undefined)}>Retour aux utilisateurs</button>}
   {scoped&&!machine?<p>Machine indisponible sur cette sélection.</p>:userId!==undefined&&!user?<p>Aucun usage de cet utilisateur sur cette période.</p>:metrics&&<>
    <div className={`${styles.consumptionMetrics} ${machine&&!user?styles.machineConsumptionMetrics:''}`}>
    {!user&&!machine&&<><article className={styles.companyQuotaCard}><h4>Quotas inclus en cours — toutes machines</h4><strong>{euro(budget)}</strong><p>{available} % disponible</p><progress className={`${styles.progress} ${color}`} aria-label="Quotas inclus disponibles" value={available} max={100}/></article><article className={styles.companyQuotaCard}><h4>Quotas inclus consommés — périodes en cours</h4><strong>{euro(used)}</strong></article></>}
    {!user&&machine&&<article className={styles.machineQuotaCard}><h4>Quota inclus de la machine — période en cours</h4><strong>{euro(used)} / {euro(budget)}</strong><p>consommés · {available} % disponible</p><progress className={`${styles.progress} ${color}`} aria-label="Quota inclus disponible" value={available} max={100}/></article>}
    {!user&&<article className={machine?styles.machineQuotaCard:styles.companyQuotaCard}><h4>{machine?'Quota inclus restant':'Quotas inclus restants — toutes machines'}</h4><strong>{euro(remaining)}</strong></article>}
        {!user&&<article className={machine?styles.machineQuotaCard:styles.companyQuotaCard}><h4>{machine?'Crédit supplémentaire débité pour cette machine':'Crédit supplémentaire consommé'}</h4><strong>{euro(commercialCredit)}</strong><p>Débits effectifs du wallet commun</p></article>}
     {user&&<><article><h4>Crédit supplémentaire consommé par cet utilisateur</h4><strong>{euro(metrics.commercialCredit)}</strong><p>Débits effectifs du wallet commun</p></article><article><h4>Coût IA réel</h4><strong>{cost(metrics)}</strong></article><article><h4>Réponses IA</h4><strong>{metrics.responses}</strong></article></>}
    </div>
    {machine&&<p>{status(machine)} · Fin des droits payés : {date(machine.rightsEndUtc)}{!machine.billable&&machine.hasPaidRights&&` · Accès maintenu jusqu’au ${date(machine.rightsEndUtc)}`}{machine.resetUtc&&` · Fin de période : ${date(machine.resetUtc)}`}</p>}
    <div className={`${styles.compactTableScroll} ${styles.consumptionDesktop}`}>
    {!scoped?<table className={styles.compactTable}><caption>Consommation par machine</caption><thead><tr>{['Machine','Statut','Quota inclus','Quota consommé','Quota restant','Crédit supplémentaire consommé','Coût IA réel','Réponses IA','Fin des droits'].map(t=><th key={t} scope="col">{t}</th>)}</tr></thead><tbody>{data.machines.map(m=><tr key={m.id??'unassigned'}><td><button className={styles.consumptionLink} onClick={()=>{setMachineId(m.id);setUserId(undefined);}}>{m.name}</button></td><td>{status(m)}{!m.billable&&m.hasPaidRights&&<p>Accès maintenu jusqu’au {date(m.rightsEndUtc)}</p>}</td><td>{euro(m.budget)}</td><td>{euro(m.used)}</td><td>{euro(m.remaining)}</td><td>{euro(m.metrics.commercialCredit)}</td><td>{cost(m.metrics)}</td><td>{m.metrics.responses}</td><td>{date(m.rightsEndUtc)}</td></tr>)}</tbody></table>:
    !user&&machine?<table className={styles.compactTable}><caption>Consommation par utilisateur — {machine.name}</caption><thead><tr>{['Utilisateur','Réponses IA','Crédit supplémentaire consommé','Coût IA réel','Vision'].map(t=><th scope="col" key={t}>{t}</th>)}</tr></thead><tbody>{machine.users.map(u=><tr key={u.id??'unassigned'}><td><button className={styles.consumptionLink} onClick={()=>setUserId(u.id)}>{u.name}</button></td><td>{u.metrics.responses}</td><td>{euro(u.metrics.commercialCredit)}</td><td>{cost(u.metrics)}</td><td>{u.metrics.vision}</td></tr>)}</tbody></table>:null}
    </div>
    <div className={styles.consumptionMobile}>{!scoped?data.machines.map(m=><article key={m.id??'unassigned'}><button className={styles.consumptionLink} onClick={()=>{setMachineId(m.id);setUserId(undefined);}}><strong>{m.name}</strong></button><p>{status(m)}</p><dl><div><dt>Quota consommé</dt><dd>{euro(m.used)} / {euro(m.budget)}</dd></div><div><dt>Quota restant</dt><dd>{euro(m.remaining)}</dd></div><div><dt>Crédit supplémentaire consommé</dt><dd>{euro(m.metrics.commercialCredit)}</dd></div><div><dt>Coût IA réel</dt><dd>{cost(m.metrics)}</dd></div><div><dt>Réponses IA</dt><dd>{m.metrics.responses}</dd></div><div><dt>Fin des droits</dt><dd>{date(m.rightsEndUtc)}</dd></div></dl></article>):!user&&machine?machine.users.map(u=><article key={u.id??'unassigned'}><button className={styles.consumptionLink} onClick={()=>setUserId(u.id)}><strong>{u.name}</strong></button><dl><div><dt>Réponses IA</dt><dd>{u.metrics.responses}</dd></div><div><dt>Crédit consommé</dt><dd>{euro(u.metrics.commercialCredit)}</dd></div><div><dt>Coût IA réel</dt><dd>{cost(u.metrics)}</dd></div><div><dt>Vision</dt><dd>{u.metrics.vision}</dd></div></dl></article>):null}</div>
   </>}
  </>}
 </section>;
}
