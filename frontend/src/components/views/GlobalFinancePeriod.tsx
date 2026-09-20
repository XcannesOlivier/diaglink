import {useEffect,useState} from 'react';
import {getApiAuthHeaders} from '../../utils/apiAuth';
import styles from './CompanyFinancePanel.module.css';
import layout from './GlobalFinancePeriod.module.css';
export type FinancePeriod='month'|'previous'|'quarter'|'year'|'custom';
export function financeBounds(period:FinancePeriod,from:string,to:string,now=new Date()){
 const year=now.getUTCFullYear(),month=now.getUTCMonth();
 let start:Date,end:Date;
 if(period==='custom'){
  if(!/^\d{4}-\d{2}-\d{2}$/.test(from)||!/^\d{4}-\d{2}-\d{2}$/.test(to)||from>to)return null;
  start=new Date(`${from}T00:00:00Z`);end=new Date(`${to}T00:00:00Z`);
  if(!Number.isFinite(+start)||!Number.isFinite(+end)||start.toISOString().slice(0,10)!==from||end.toISOString().slice(0,10)!==to)return null;
  end.setUTCDate(end.getUTCDate()+1);
 }else{
  start=new Date(Date.UTC(year,period==='year'?0:period==='previous'?month-1:period==='quarter'?month-2:month,1));
  end=period==='previous'?new Date(Date.UTC(year,month,1)):now;
 }
 return {from:start.toISOString(),to:end.toISOString()};
}
type Amounts={subscriptionsPaidEur:number;includedCreditGrantedEur:number;topUpsAddedEur:number;companies:number;machines:number;users:number};
export function GlobalFinancePeriod({token}:{token:()=>Promise<string|null>}){
 const [period,setPeriod]=useState<FinancePeriod>('month'),[from,setFrom]=useState(''),[to,setTo]=useState('');
 const [data,setData]=useState<Amounts|null>(null),[error,setError]=useState('');
 const [selectedBounds,setSelectedBounds]=useState<{from:string;to:string}|null>(null);
 useEffect(()=>{const controller=new AbortController();setData(null);setError('');
  const bounds=financeBounds(period,from,to);
  setSelectedBounds(bounds);
  if(!bounds){setError('Choisissez une période valide.');return()=>controller.abort();}
  void(async()=>{try{
   const {headers}=await getApiAuthHeaders(token);
   const response=await fetch(`${import.meta.env.VITE_API_URL||'/api'}/admin/finance/period?${new URLSearchParams(bounds)}`,{headers,signal:controller.signal});
   if(!response.ok)throw new Error();const value=await response.json();if(!controller.signal.aborted)setData(value);
  }catch{if(!controller.signal.aborted)setError('Indicateurs financiers indisponibles.');}})();
  return()=>controller.abort();
 },[token,period,from,to]);
 const date=(value:string)=>new Date(value).toLocaleDateString('fr-FR',{day:'numeric',month:'long',year:'numeric',timeZone:'UTC'});
 return <section aria-label="Statistiques sur période" className={styles.admin}>
  {selectedBounds&&<h3>Période : {date(selectedBounds.from)} → {date(new Date(new Date(selectedBounds.to).getTime()-1).toISOString())}</h3>}
  <label>Période <select value={period} onChange={e=>setPeriod(e.target.value as FinancePeriod)}>
   <option value="month">Ce mois-ci</option><option value="previous">Mois dernier</option><option value="quarter">3 derniers mois</option><option value="year">Cette année</option><option value="custom">Période personnalisée</option>
  </select></label>
  {period==='custom'&&<div className={styles.buttons}><label>Du <input type="date" value={from} onChange={e=>setFrom(e.target.value)}/></label><label>Au <input type="date" value={to} onChange={e=>setTo(e.target.value)}/></label></div>}
  {error?<p role="alert">{error}</p>:!data?<p>Chargement…</p>:<div className={layout.grid}>
   {([
    ['Entreprises',data.companies,false],
    ['Machines',data.machines,false],
    ['Utilisateurs',data.users,false],
    ['Abonnements encaissés',data.subscriptionsPaidEur,true],
    ['Crédit Agent inclus attribué',data.includedCreditGrantedEur,true],
    ['Crédit supplémentaire ajouté',data.topUpsAddedEur,true]
   ] as const).map(([label,value,currency])=><article key={label} className={layout.card}><h4>{label}</h4><strong>{new Intl.NumberFormat('fr-FR',currency?{style:'currency',currency:'EUR',minimumFractionDigits:2,maximumFractionDigits:2}:{}).format(value)}</strong></article>)}
  </div>}
 </section>;
}
