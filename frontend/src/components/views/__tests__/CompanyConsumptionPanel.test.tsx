import {it,expect,vi,afterEach} from 'vitest';
import {act} from 'react';import {createRoot} from 'react-dom/client';
import {CompanyConsumptionPanel} from '../CompanyConsumptionPanel';
vi.mock('../../../utils/apiAuth',()=>({getApiAuthHeaders:async()=>({headers:{}})}));
const metrics={responses:16,vision:2,summaries:1,input:100,output:20,tokens:120,unknown:1,unvalued:1,realCost:3.18,commercialCredit:2.4,walletRealCost:.8,providers:[{provider:'test',model:'model',count:3}]};
const machine=(id:string,name:string,n:number)=>({id,name,billable:id==='a',budget:10,used:10,remaining:0,resetUtc:'2026-11-14T00:00:00Z',rightsEndUtc:'2026-11-14T00:00:00Z',hasPaidRights:true,metrics:{...metrics,responses:n},users:[{id:'u',name:'Olivier',metrics:{...metrics,responses:n}}]});
const report={companyName:'Entreprise',walletBalance:49.96,metrics,machines:[machine('a','Machine A',16),machine('b','Machine B',999)]};
afterEach(()=>vi.unstubAllGlobals());
it('keeps company wallet separate and drills down users within their machine with filters',async()=>{
 const fetch=vi.fn().mockImplementation(async()=>new Response(JSON.stringify(report)));vi.stubGlobal('fetch',fetch);
 const host=document.createElement('div'),root=createRoot(host),token=async()=>null;
 const click=async(text:string)=>act(async()=>[...host.querySelectorAll('button')].find(b=>b.textContent===text)!.click());
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={token}/>));
 expect(host.textContent).not.toContain('49,96');expect(host.textContent).toContain('20,00');
 expect([...host.querySelectorAll('div[class*="consumptionMetrics"] article h4')].map(heading=>heading.textContent)).toEqual([
  'Quota inclus total','Quota restant','Crédit supplémentaire consommé'
 ]);
 expect(host.textContent).toContain('4,80');
 expect(host.querySelector('details')?.open).toBe(false);
 expect(host.querySelector('table')?.textContent).toContain('Accès maintenu');
 await click('Machine A');expect(host.textContent).not.toContain('999');
 expect([...host.querySelectorAll('div[class*="consumptionMetrics"] article h4')].map(heading=>heading.textContent)).toEqual([
  'Quota inclus de cette machine','Quota restant','Crédit supplémentaire consommé par cette machine'
 ]);
 await click('Olivier');expect(host.querySelector('nav')?.textContent).toBe('Entreprise > Machine A > Olivier');
 expect(host.textContent).toContain('2,40');expect(host.textContent).toContain('3,18');expect(host.textContent).not.toContain('(partiel)');
 expect(host.textContent).not.toContain('999');expect(host.textContent).not.toContain('Crédit supplémentaire commun disponible');
 await act(async()=>{const select=host.querySelector<HTMLSelectElement>('select[aria-label="Type d’usage"]')!;select.value='VisionTool';select.dispatchEvent(new Event('change',{bubbles:true}));});
 expect(fetch.mock.lastCall?.[0]).toContain('usageType=VisionTool');
 await click('Retour aux machines');await click('Machine B');await click('Olivier');expect(host.textContent).toContain('999');
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
