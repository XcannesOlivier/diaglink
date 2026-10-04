import {afterEach,it,expect,vi} from 'vitest';
import {act} from 'react';
import {createRoot} from 'react-dom/client';
import {CompanyFinancePanel} from '../CompanyFinancePanel';
import {getMachines} from '../../../services/machineService';
vi.mock('../../../services/machineService',()=>({getMachines:vi.fn()}));
vi.mock('../../../services/companyFinanceService',()=>({financeRequest:async(_:unknown,path:string)=>({kind:'success',data:path==='/topups'?[]:{walletBalance:20,unpaidInvoiceAmount:null,subscriptionStatus:'active'}}),companyConsumptionRequest:async()=>({kind:'success',data:{machines:[]}})}));
vi.mock('../CompanyMachineQuota',()=>({CompanyMachineQuota:({machine}:{machine:{name:string}})=><li>{machine.name}</li>}));
afterEach(()=>localStorage.clear());
it.each([1,2,3,4,5,6,12])('limits %s machines and preserves order through expansion/collapse',async(count)=>{
 const machines=Array.from({length:count},(_,i)=>({id:`m${i}`,companyId:'c',name:`Machine ${count-i}`,status:'active',reference:null,hasAssistantConfigured:false,isAccessible:true}));
 vi.mocked(getMachines).mockResolvedValue({kind:'success',data:machines});
 const host=document.createElement('div'),root=createRoot(host);document.body.append(host);
 try{
  await act(async()=>root.render(<CompanyFinancePanel companyId="c" getAccessToken={async()=>null}/>));
  const card=host.querySelector('[aria-label="Machines et quotas"]')!;
  const names=()=>[...card.querySelectorAll('li')].map(li=>li.textContent);
  expect(names()).toEqual(machines.slice(0,5).map(m=>m.name));
  const button=card.querySelector('button');
  if(count<=5){expect(button).toBeNull();return;}
  expect(button?.textContent).toBe('Voir plus de machines');expect(button?.getAttribute('aria-expanded')).toBe('false');
  await act(async()=>button!.click());
  expect(names()).toEqual(machines.map(m=>m.name));expect(button?.textContent).toBe('Voir moins de machines');expect(button?.getAttribute('aria-expanded')).toBe('true');
  await act(async()=>button!.click());
  expect(names()).toEqual(machines.slice(0,5).map(m=>m.name));expect(button?.textContent).toBe('Voir plus de machines');
 }finally{await act(async()=>root.unmount());host.remove();}
});
it('filters every machine by trimmed case-insensitive name without the five-machine limit',async()=>{
 const machines=Array.from({length:7},(_,index)=>({id:`m${index}`,companyId:'c',name:`Tracteur ${index+1}`,status:'active',reference:null,hasAssistantConfigured:false,isAccessible:true}));
 vi.mocked(getMachines).mockResolvedValue({kind:'success',data:machines});
 const host=document.createElement('div'),root=createRoot(host);document.body.append(host);
 try{
  await act(async()=>root.render(<CompanyFinancePanel companyId="c" getAccessToken={async()=>null}/>));
  const card=host.querySelector('[aria-label="Machines et quotas"]')!;
  const search=card.querySelector<HTMLInputElement>('[aria-label="Rechercher une machine"]')!;
  const setSearch=async(value:string)=>act(async()=>{const setter=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value')!.set!;setter.call(search,value);search.dispatchEvent(new Event('input',{bubbles:true}));});
  await setSearch('  TRACT  ');
  expect(card.querySelectorAll('li')).toHaveLength(7);
  expect([...card.querySelectorAll('button')].some(button=>button.textContent?.includes('Voir plus'))).toBe(false);
  await setSearch('inconnue');
  expect(card.textContent).toContain('Aucune machine trouvée.');
  expect(card.querySelectorAll('li')).toHaveLength(0);
 }finally{await act(async()=>root.unmount());host.remove();}
});
