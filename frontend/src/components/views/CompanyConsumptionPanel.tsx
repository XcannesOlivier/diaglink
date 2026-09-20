import {useEffect,useState} from 'react';
import {createPortal} from 'react-dom';
import {getApiAuthHeaders} from '../../utils/apiAuth';
import {usagePeriods,usageWindow,type UsagePeriod} from '../../utils/aiUsage';
import type {UsageType} from '../../types/aiUsage';
import styles from './CompanyFinancePanel.module.css';

type Metrics={responses:number;vision:number;summaries:number;input:number;output:number;tokens:number;unknown:number;unvalued:number;realCost:number;commercialCredit:number;walletRealCost:number;providers:{provider:string|null;model:string|null;count:number}[]};
type User={id:string|null;name:string;metrics:Metrics};
type Machine={id:string|null;name:string;billable:boolean;budget:number;used:number;remaining:number;resetUtc:string|null;rightsEndUtc:string|null;hasPaidRights:boolean;metrics:Metrics;users:User[]};
type Report={companyName:string;walletBalance:number;metrics:Metrics;machines:Machine[]};
const euro=(n:number)=>new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(n);
const date=(s:string|null)=>s?new Date(s.endsWith('Z')?s:`${s}Z`).toLocaleDateString('fr-FR',{day:'numeric',month:'long',year:'numeric',timeZone:'UTC'}):'Non disponible';
const status=(m:Machine)=>`${m.billable?'Active · facturable':'Non renouvelée'}${!m.resetUtc?' · Quota mensuel non actif':''}`;
const cost=(m:Metrics)=>euro(m.realCost);

export function CompanyConsumptionPanel({companyId,token,revision=0,diagnosticTarget}:{companyId:string;token:()=>Promise<string|null>;revision?:number;diagnosticTarget?:HTMLElement|null}){
 const [period,setPeriod]=useState<UsagePeriod>('month'),[type,setType]=useState<UsageType|undefined>();
 const [filter,setFilter]=useState(()=>usageWindow('month'));
 const [machineId,setMachineId]=useState<string|null|undefined>(),[userId,setUserId]=useState<string|null|undefined>();
 const [data,setData]=useState<Report|null>(null),[error,setError]=useState(false);
 useEffect(()=>{setMachineId(undefined);setUserId(undefined);},[companyId]);
 useEffect(()=>{const c=new AbortController();setData(null);setError(false);
  void(async()=>{try{const {headers}=await getApiAuthHeaders(token);const query=new URLSearchParams({to:filter.to});if(filter.from)query.set('from',filter.from);if(filter.usageType)query.set('usageType',filter.usageType);
   const r=await fetch(`${import.meta.env.VITE_API_URL||'/api'}/companies/${companyId}/stripe/finance/consumption?${query}`,{headers,signal:c.signal});if(!r.ok)throw new Error();const value=await r.json();if(!c.signal.aborted)setData(value);
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
 const diagnostic=metrics?<section aria-label="Coûts et détails techniques">
     <h4>Coûts et détails techniques</h4>
     <p>Coûts recalculés avec les tarifs et taux existants. Les montants partiels excluent les usages non valorisables.</p>
     <dl className={styles.details}>{[['Coût IA réel fournisseur',cost(metrics)],['Crédit commercial consommé',euro(metrics.commercialCredit)],['Coût IA couvert par le wallet',euro(metrics.walletRealCost)],['Marge wallet estimée',euro(metrics.commercialCredit-metrics.walletRealCost)],['Input',metrics.input],['Output',metrics.output],['Tokens connus',metrics.tokens],['Analyses Vision',metrics.vision],['Résumés internes',metrics.summaries],['Usages inconnus',metrics.unknown],['Usages non valorisés / non convertis',metrics.unvalued]].map(([label,value])=><div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>
     <ul>{metrics.providers.map((p,i)=><li key={i}>{p.provider??'Non renseigné'} / {p.model??'Non renseigné'} : {p.count} usages</li>)}</ul>
    </section>:null;
 return <section aria-label="Suivi de consommation" className={styles.panel}>
  <div className={styles.buttons}>
   <label>Période <select aria-label="Période de consommation" value={period} onChange={e=>{const p=e.target.value as UsagePeriod;setPeriod(p);setFilter(usageWindow(p,type));}}>{Object.entries(usagePeriods).map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></label>
   <label>Type d’usage <select aria-label="Type d’usage" value={type??''} onChange={e=>{const t=(e.target.value||undefined) as UsageType|undefined;setType(t);setFilter(usageWindow(period,t));}}><option value="">Tous les usages</option><option value="ChatResponse">Utilisateur</option><option value="VisionTool">Vision</option><option value="ConversationSummary">Interne</option></select></label>
   <button type="button" className={styles.machineToggle} onClick={()=>setFilter(usageWindow(period,type))}>Actualiser</button>
  </div>
  {error?<p role="alert">Suivi indisponible. Réessayez.</p>:!data?<p>Chargement…</p>:<>
   {scoped&&<nav aria-label="Fil d’Ariane consommation" className={styles.consumptionBreadcrumb}>
    <span className={styles.breadcrumbCompany}>{data.companyName}</span>
    {machine&&<><span className={styles.breadcrumbSeparator} aria-hidden="true">{' > '}</span><strong className={styles.breadcrumbMachine}>{machine.name}</strong></>}
    {user&&<><span className={styles.breadcrumbSeparator} aria-hidden="true">{' > '}</span><span className={styles.breadcrumbUser}>{user.name}</span></>}
   </nav>}
   {scoped&&<button className={styles.machineToggle} onClick={()=>{setMachineId(undefined);setUserId(undefined);}}>Retour aux machines</button>}
   {userId!==undefined&&<button className={styles.machineToggle} onClick={()=>setUserId(undefined)}>Retour aux utilisateurs</button>}
   {scoped&&!machine?<p>Machine indisponible sur cette sélection.</p>:userId!==undefined&&!user?<p>Aucun usage de cet utilisateur sur cette période.</p>:metrics&&<>
    <div className={`${styles.consumptionMetrics} ${!user?styles.machineConsumptionMetrics:''}`}>
     {!user&&<article><h4>{machine?'Quota inclus de cette machine':'Quota inclus total'}</h4><strong>{euro(used)} / {euro(budget)}</strong><p>consommés · {available} % disponible</p><progress className={`${styles.progress} ${color}`} aria-label="Quota inclus disponible" value={available} max={100}/></article>}
     {!user&&<article><h4>Quota restant</h4><strong>{euro(remaining)}</strong></article>}
     {!user&&<article><h4>Crédit supplémentaire consommé{machine?' par cette machine':''}</h4><strong>{euro(commercialCredit)}</strong><p>Débits effectifs du wallet commun</p></article>}
     {user&&<><article><h4>Crédit supplémentaire consommé par cet utilisateur</h4><strong>{euro(metrics.commercialCredit)}</strong><p>Débits effectifs du wallet commun</p></article><article><h4>Coût IA réel</h4><strong>{cost(metrics)}</strong></article><article><h4>Réponses IA</h4><strong>{metrics.responses}</strong></article></>}
    </div>
    {machine&&<p>{status(machine)} · Fin des droits payés : {date(machine.rightsEndUtc)}{!machine.billable&&machine.hasPaidRights&&` · Accès maintenu jusqu’au ${date(machine.rightsEndUtc)}`}{machine.resetUtc&&` · Fin de période : ${date(machine.resetUtc)}`}</p>}
    <div className={`${styles.compactTableScroll} ${styles.consumptionDesktop}`}>
    {!scoped?<table className={styles.compactTable}><caption>Consommation par machine</caption><thead><tr>{['Machine','Statut','Quota inclus','Quota consommé','Quota restant','Crédit supplémentaire consommé','Coût IA réel','Réponses IA','Fin des droits'].map(t=><th key={t} scope="col">{t}</th>)}</tr></thead><tbody>{data.machines.map(m=><tr key={m.id??'unassigned'}><td><button className={styles.consumptionLink} onClick={()=>{setMachineId(m.id);setUserId(undefined);}}>{m.name}</button></td><td>{status(m)}{!m.billable&&m.hasPaidRights&&<p>Accès maintenu jusqu’au {date(m.rightsEndUtc)}</p>}</td><td>{euro(m.budget)}</td><td>{euro(m.used)}</td><td>{euro(m.remaining)}</td><td>{euro(m.metrics.commercialCredit)}</td><td>{cost(m.metrics)}</td><td>{m.metrics.responses}</td><td>{date(m.rightsEndUtc)}</td></tr>)}</tbody></table>:
     !user&&machine?<table className={styles.compactTable}><caption>Utilisateurs de {machine.name}</caption><thead><tr>{['Utilisateur','Réponses IA','Crédit supplémentaire consommé','Coût IA réel','Vision'].map(t=><th scope="col" key={t}>{t}</th>)}</tr></thead><tbody>{machine.users.map(u=><tr key={u.id??'unassigned'}><td><button className={styles.consumptionLink} onClick={()=>setUserId(u.id)}>{u.name}</button></td><td>{u.metrics.responses}</td><td>{euro(u.metrics.commercialCredit)}</td><td>{cost(u.metrics)}</td><td>{u.metrics.vision}</td></tr>)}</tbody></table>:null}
    </div>
    <div className={styles.consumptionMobile}>{!scoped?data.machines.map(m=><article key={m.id??'unassigned'}><button className={styles.consumptionLink} onClick={()=>{setMachineId(m.id);setUserId(undefined);}}><strong>{m.name}</strong></button><p>{status(m)}</p><dl><div><dt>Quota consommé</dt><dd>{euro(m.used)} / {euro(m.budget)}</dd></div><div><dt>Quota restant</dt><dd>{euro(m.remaining)}</dd></div><div><dt>Crédit supplémentaire consommé</dt><dd>{euro(m.metrics.commercialCredit)}</dd></div><div><dt>Coût IA réel</dt><dd>{cost(m.metrics)}</dd></div><div><dt>Réponses IA</dt><dd>{m.metrics.responses}</dd></div><div><dt>Fin des droits</dt><dd>{date(m.rightsEndUtc)}</dd></div></dl></article>):!user&&machine?machine.users.map(u=><article key={u.id??'unassigned'}><button className={styles.consumptionLink} onClick={()=>setUserId(u.id)}><strong>{u.name}</strong></button><dl><div><dt>Réponses IA</dt><dd>{u.metrics.responses}</dd></div><div><dt>Crédit consommé</dt><dd>{euro(u.metrics.commercialCredit)}</dd></div><div><dt>Coût IA réel</dt><dd>{cost(u.metrics)}</dd></div><div><dt>Vision</dt><dd>{u.metrics.vision}</dd></div></dl></article>):null}</div>
    {diagnosticTarget?createPortal(diagnostic,diagnosticTarget):diagnosticTarget===undefined?<details className={styles.technical}><summary>Coûts et détails techniques</summary>{diagnostic}</details>:null}
   </>}
  </>}
 </section>;
}
