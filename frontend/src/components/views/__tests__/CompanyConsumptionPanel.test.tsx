import {it,expect,vi,afterEach} from 'vitest';
import {act} from 'react';import {createRoot} from 'react-dom/client';
import {CompanyConsumptionPanel} from '../CompanyConsumptionPanel';
import styles from '../CompanyFinancePanel.module.css';
vi.mock('../../../utils/apiAuth',()=>({getApiAuthHeaders:async()=>({headers:{}})}));
const metrics={responses:16,vision:2,summaries:1,input:100,output:20,tokens:120,unknown:1,unvalued:1,realCost:3.18,commercialCredit:2.4,walletRealCost:.8,providers:[{provider:'test',model:'model',count:3}]};
const machine=(id:string,name:string,n:number)=>({id,name,billable:id==='a',budget:10,used:10,remaining:0,resetUtc:'2026-11-14T00:00:00Z',rightsEndUtc:'2026-11-14T00:00:00Z',hasPaidRights:true,metrics:{...metrics,responses:n},users:[{id:'u',name:'Olivier',metrics:{...metrics,responses:n}}]});
const report={companyName:'Entreprise',walletBalance:49.96,metrics,machines:[machine('a','Machine A',16),machine('b','Machine B',999)]};
afterEach(()=>vi.unstubAllGlobals());
it('keeps company wallet separate and drills down users with business labels',async()=>{
 const fetch=vi.fn().mockImplementation(async()=>new Response(JSON.stringify(report)));vi.stubGlobal('fetch',fetch);
 const host=document.createElement('div'),root=createRoot(host),token=async()=>null;
 const click=async(text:string)=>act(async()=>[...host.querySelectorAll('button')].find(b=>b.textContent===text)!.click());
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={token}/>));
 expect(host.textContent).not.toContain('49,96');expect(host.textContent).toContain('20,00');
 expect([...host.querySelectorAll('div[class*="consumptionMetrics"] article h4')].map(heading=>heading.textContent)).toEqual([
    'Quotas inclus en cours — toutes machines','Quotas inclus consommés — périodes en cours',
    'Quotas inclus restants — toutes machines','Crédit supplémentaire consommé'
 ]);
 const companyCards=[...host.querySelectorAll('div[class*="consumptionMetrics"] article')];
 expect(companyCards.every(card=>card.classList.contains(styles.companyQuotaCard))).toBe(true);
 expect(companyCards.every(card=>!card.classList.contains(styles.machineQuotaCard))).toBe(true);
 expect(host.textContent).toContain('4,80');
 expect(host.querySelector('select[aria-label="Période de consommation"]')).not.toBeNull();
 expect(host.textContent).toContain('Tous les usages');
 expect(host.querySelector('select[aria-label="Type d’usage"]')).toBeNull();
 expect(host.textContent).not.toContain('Analyse détaillée des usages');
 expect(host.textContent).not.toContain('Coûts et détails techniques');
 expect(host.querySelector('table')?.textContent).toContain('Accès maintenu');
 await click('Machine A');expect(host.textContent).not.toContain('999');
 expect([...host.querySelectorAll('div[class*="consumptionMetrics"] article h4')].map(heading=>heading.textContent)).toEqual([
    'Quota inclus de la machine — période en cours','Quota inclus restant','Crédit supplémentaire débité pour cette machine'
 ]);
 const machineCards=[...host.querySelectorAll('div[class*="consumptionMetrics"] article')];
 expect(machineCards.every(card=>card.classList.contains(styles.machineQuotaCard))).toBe(true);
 expect(machineCards.every(card=>!card.classList.contains(styles.companyQuotaCard))).toBe(true);
 expect(host.querySelector('table caption')?.textContent).toBe('Consommation par utilisateur — Machine A');
 await click('Olivier');expect(host.querySelector('nav')?.textContent).toBe('Entreprise > Machine A > Olivier');
 expect(host.textContent).toContain('2,40');expect(host.textContent).toContain('3,18');expect(host.textContent).not.toContain('(partiel)');
 expect(host.textContent).not.toContain('999');expect(host.textContent).not.toContain('Crédit supplémentaire commun disponible');
 await click('Retour aux machines');await click('Machine B');await click('Olivier');expect(host.textContent).toContain('999');
 }finally{await act(async()=>root.unmount());}
});
it('refreshes the complete business view by period without usage type filtering',async()=>{
 const allTime={...report,machines:report.machines.map(item=>({...item,budget:20,used:12,remaining:8}))};
 const fetch=vi.fn().mockImplementation(async(input:string)=>new Response(JSON.stringify(input.includes('from=')?report:allTime)));vi.stubGlobal('fetch',fetch);
 const host=document.createElement('div'),root=createRoot(host);
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={async()=>null}/>));
  expect(String(fetch.mock.calls[0][0])).toContain('from=');
  expect(fetch.mock.calls.every(call=>!String(call[0]).includes('usageType='))).toBe(true);
  const period=host.querySelector<HTMLSelectElement>('select[aria-label="Période de consommation"]')!;
  expect([...period.options].map(option=>option.value)).toEqual(['today','week','month','previousMonth','thirtyDays','all']);
  await act(async()=>{period.value='all';period.dispatchEvent(new Event('change',{bubbles:true}));});
  expect(String(fetch.mock.lastCall?.[0])).not.toContain('from=');
  expect(String(fetch.mock.lastCall?.[0])).not.toContain('usageType=');
  expect([...host.querySelectorAll('div[class*="consumptionMetrics"] article')].find(card=>card.querySelector('h4')?.textContent==='Quotas inclus en cours — toutes machines')?.querySelector('strong')?.textContent).toBe('40,00 €');
  const calls=fetch.mock.calls.length;
  await act(async()=>[...host.querySelectorAll('button')].find(button=>button.textContent==='Actualiser')!.click());
  expect(fetch).toHaveBeenCalledTimes(calls+1);
  expect(String(fetch.mock.lastCall?.[0])).not.toContain('usageType=');
 }finally{await act(async()=>root.unmount());}
});
it('discards late responses after switching company',async()=>{
 let finish!:(v:Response)=>void;
 const fetch=vi.fn().mockImplementationOnce(()=>new Promise(r=>{finish=r;})).mockImplementation(async()=>new Response(JSON.stringify({...report,companyName:'New company',machines:[]})));vi.stubGlobal('fetch',fetch);
 const host=document.createElement('div'),root=createRoot(host),token=async()=>null;
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="old" token={token}/>));await act(async()=>root.render(<CompanyConsumptionPanel companyId="new" token={token}/>));
 await act(async()=>finish(new Response(JSON.stringify(report))));expect(host.textContent).not.toContain('Machine A');expect(fetch.mock.calls.some(call=>String(call[0]).includes('/companies/new/'))).toBe(true);
 }finally{await act(async()=>root.unmount());}
});
it('renders assigned users with zero consumption alongside usage and historical rows',async()=>{
 const zero={responses:0,vision:0,summaries:0,input:0,output:0,tokens:0,unknown:0,unvalued:0,realCost:0,commercialCredit:0,walletRealCost:0,providers:[]};
 const users=[{id:'used',name:'Robert Petit',metrics},{id:'zero',name:'Technicien sans usage',metrics:zero},{id:'deleted',name:'Utilisateur non attribué / supprimé',metrics:{...zero,vision:1}}];
 const value={...report,machines:[{...machine('a','Machine A',16),users}]};
 vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response(JSON.stringify(value))));
 const host=document.createElement('div'),root=createRoot(host);
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={async()=>null}/>));
 await act(async()=>[...host.querySelectorAll('button')].find(button=>button.textContent==='Machine A')!.click());
 const rows=[...host.querySelectorAll('table tbody tr')].map(row=>row.textContent);
 expect(rows).toHaveLength(3);expect(rows[0]).toContain('Robert Petit');
 expect(rows[1]).toContain('Technicien sans usage');expect(rows[1]).toContain('0,00 €');
 expect(rows[2]).toContain('Utilisateur non attribué / supprimé');expect(rows[2]).toContain('1');
 }finally{await act(async()=>root.unmount());}
});
