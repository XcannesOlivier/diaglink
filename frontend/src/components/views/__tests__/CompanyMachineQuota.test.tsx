import {act} from 'react';
import {createRoot,type Root} from 'react-dom/client';
import {afterEach,beforeEach,describe,expect,it,vi} from 'vitest';
import {CompanyMachineQuota} from '../CompanyMachineQuota';

const {financeRequest}=vi.hoisted(()=>({financeRequest:vi.fn()}));
vi.mock('../../../services/companyFinanceService',()=>({financeRequest}));
vi.mock('../MachineUserConsumption',()=>({MachineUserConsumption:()=>null}));

const machines=[
 {id:'m1',companyId:'c1',name:'Machine A',reference:null,status:'active',hasAssistantConfigured:true,isAccessible:true},
 {id:'m2',companyId:'c1',name:'Machine B',reference:null,status:'active',hasAssistantConfigured:true,isAccessible:true},
];
const consumptions=[
 {id:'m1',name:'Machine A',billable:true,hasPaidRights:true,includedQuotaBudget:10,resetUtc:'2026-10-14T00:00:00Z',commercialCredit:0,users:[]},
 {id:'m2',name:'Machine B',billable:false,hasPaidRights:false,includedQuotaBudget:0,resetUtc:null,commercialCredit:0,users:[]},
];

describe('CompanyMachineQuota responsive state accordions',()=>{
 let host:HTMLDivElement,root:Root;
 const originalMatchMedia=window.matchMedia;
 const setMobile=(mobile:boolean)=>Object.defineProperty(window,'matchMedia',{configurable:true,value:(query:string)=>({matches:mobile&&query==='(max-width: 767px)',media:query,onchange:null,addListener(){},removeListener(){},addEventListener(){},removeEventListener(){},dispatchEvent(){return true;}})});

 beforeEach(()=>{
  financeRequest.mockReset().mockResolvedValue({kind:'success',data:{machineCreditResetUtc:'2026-10-14T00:00:00Z',machineCreditRemaining:0}});
  host=document.createElement('div');document.body.append(host);root=createRoot(host);
 });
 afterEach(async()=>{await act(async()=>root.unmount());host.remove();Object.defineProperty(window,'matchMedia',{configurable:true,value:originalMatchMedia});});

 const renderCards=async()=>act(async()=>root.render(<ul>{machines.map((machine,index)=><CompanyMachineQuota key={machine.id} machine={machine} consumption={consumptions[index]} usersId={`users-${machine.id}`} onToggleUsers={()=>undefined} token={async()=>null} revision={0}/>)}</ul>));

 it('keeps each mobile state block closed by default and toggles them independently',async()=>{
  setMobile(true);await renderCards();
  const states=[...host.querySelectorAll<HTMLElement>('article[aria-label^="État de "]')];
  const toggles=states.map(state=>state.querySelector<HTMLButtonElement>('button')!);
  expect(toggles.map(toggle=>toggle.getAttribute('aria-expanded'))).toEqual(['false','false']);
  expect(states.map(state=>state.textContent)).toEqual(['État','État']);

  await act(async()=>toggles[0].click());
  expect(toggles[0].getAttribute('aria-expanded')).toBe('true');
  expect(states[0].textContent).toContain('Abonnement : Actif');
  expect(toggles[1].getAttribute('aria-expanded')).toBe('false');
  expect(states[1].textContent).toBe('État');

  await act(async()=>toggles[1].click());
  expect(states[1].textContent).toContain('Abonnement : Inactif');
  await act(async()=>toggles[0].click());
  expect(toggles[0].getAttribute('aria-expanded')).toBe('false');
  expect(states[0].textContent).toBe('État');
 });

 it('keeps the existing non-accordion state presentation from 768px upward',async()=>{
  setMobile(false);await renderCards();
  const states=[...host.querySelectorAll<HTMLElement>('article[aria-label^="État de "]')];
  expect(states.every(state=>state.querySelector('button')===null)).toBe(true);
  expect(states[0].textContent).toContain('Abonnement : Actif');
  expect(states[1].textContent).toContain('Abonnement : Inactif');
 });
});
