import styles from '../CompanyFinancePanel.module.css';
﻿import {beforeEach,afterEach,it,expect,vi} from 'vitest';
import {act} from 'react';import {createRoot,type Root} from 'react-dom/client';
import {CompanyFinancePanel} from '../CompanyFinancePanel';
const {request}=vi.hoisted(()=>({request:vi.fn()}));
vi.mock('../../../services/companyFinanceService',()=>({financeRequest:request}));
vi.mock('../../../services/machineService',()=>({getMachines:async()=>({kind:'success',data:[{id:'m',name:'Compresseur',companyId:'c',status:'active'},{id:'i',name:'Inactive',companyId:'c',status:'inactive'},{id:'other',name:'Foreign',companyId:'other',status:'active'}]})}));
let box:HTMLDivElement,root:Root;
const token=async()=>null;
const summary={machineCreditResetUtc:null,machineCreditRemaining:0,walletBalance:20,subscriptionStatus:'past_due',nextDueUtc:'2026-11-14T18:28:51Z',cancelAtPeriodEnd:false,unpaidInvoiceAmount:29.90,rechargeEnabled:true,creditStatus:'AiCreditExhausted'};
it.each([false,true])('shows cancellation only when scheduled: %s',async(cancelAtPeriodEnd)=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[]:{...summary,cancelAtPeriodEnd}}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" getAccessToken={token}/>));
 const subscription=box.querySelector<HTMLDetailsElement>('[aria-label="Abonnement"]')!;
 expect(subscription.open).toBe(false);
 await act(async()=>subscription.querySelector('summary')!.click());expect(subscription.open).toBe(true);
 if(cancelAtPeriodEnd)expect(subscription.textContent).toContain('Résiliation prévue à l’échéance');
 else expect(subscription.textContent).not.toContain('Résiliation prévue');
 expect([...subscription.querySelectorAll('dd')].map(e=>e.textContent)).not.toContain('Non');
 const inactive=box.querySelector('[aria-label="Quota Inactive"]')!;
 expect(inactive.querySelector('time')).toBeNull();expect(inactive.textContent).not.toContain('Réinitialisation');
 expect(box.querySelector('[aria-label="Quota Compresseur"]')?.textContent).toContain('Quota mensuel non actif');
});
it('lists all company machines with independent quotas regardless of chat selection',async()=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[]:{...summary,machineCreditRemaining:path==='?machineId=m'?6.4:0,machineCreditResetUtc:path==='?machineId=m'?'2026-10-14T00:00:00Z':null}}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" machine={{id:'other',name:'Chat selection'}} getAccessToken={token}/>));
 expect(box.querySelector('[aria-label="Quota Compresseur"]')?.textContent).toContain('64 % disponible');
 expect(box.querySelector('[aria-label="Quota Inactive"]')?.textContent).toContain('Non renouvelée');
 expect(box.querySelector('[aria-label="Quota Inactive"]')?.textContent).toContain('Aucune période active');
 expect(box.textContent).not.toContain('Foreign');expect(box.textContent).not.toContain('Chat selection');
 expect(request).not.toHaveBeenCalledWith(token,'?machineId=other');
 expect(box.textContent).toContain('Nombre total de machines');expect(box.querySelectorAll('li[aria-label^="Quota "]')).toHaveLength(2);
});
it.each([[10,100],[6,60],[6.4,64],[0,0]])('shows monthly credit only as percentages for remaining %s',async(remaining,available)=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[]:{...summary,machineCreditRemaining:remaining,machineCreditResetUtc:'2026-10-14T18:28:51Z'}}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" machine={{id:'m',name:'Machine'}} getAccessToken={token}/>));
 const card=box.querySelector('[aria-label="Quota Compresseur"]')!;
 expect(card.textContent).toContain(`${available} % disponible`);
 expect(card.textContent).toContain(remaining===0?'Quota mensuel utilisé':'Quota mensuel');
 expect(card.textContent).not.toMatch(/€|EUR|10,00/);
 expect(card.querySelector('progress')?.value).toBe(available);
 const inactive=box.querySelector('[aria-label="Quota Inactive"]')!;
 expect(inactive.textContent).toContain('Non renouvelée');
 expect(inactive.textContent).toContain('Accès maintenu jusqu’au 14 octobre 2026');
 expect(inactive.textContent).not.toContain('Réinitialisation');
 expect(inactive.textContent).not.toContain('Désactivée');
 expect(inactive.querySelector('progress')?.value).toBe(available);
 expect(box.querySelector('[aria-label="Crédit supplémentaire"]')?.textContent).toContain('20,00');
});
it('opens the server invoice URL in the current tab',async()=>{
 const assign=vi.fn();vi.stubGlobal('location',{assign});
 try{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve(path==='/invoice-payment'?{kind:'success',data:{paymentUrl:'https://invoice.stripe.com/i/owned'}}:{kind:'success',data:path==='/topups'?[]:summary}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" getAccessToken={token}/>));
 await act(async()=>[...box.querySelectorAll('button')].find(b=>b.textContent==='Régler la facture')!.click());
 expect(assign).toHaveBeenCalledExactlyOnceWith('https://invoice.stripe.com/i/owned');
 }finally{vi.unstubAllGlobals();}
});
it('requests the company invoice without an invoice ID or client URL and rejects unsafe redirects',async()=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve(path==='/invoice-payment'?{kind:'success',data:{paymentUrl:'https://evil.example'}}:{kind:'success',data:path==='/topups'?[]:summary}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" getAccessToken={token}/>));
 await act(async()=>[...box.querySelectorAll('button')].find(b=>b.textContent==='Régler la facture')!.click());
 expect(request).toHaveBeenCalledWith(token,'/invoice-payment',{});
 expect(box.textContent).toContain('Facture indisponible');
 expect(box.textContent).toContain('Une recharge de crédit ne règle pas cette facture.');
});
it('never presents included credit as available without an active period, even with a funded wallet',async()=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[]:{...summary,machineCreditRemaining:10,creditStatus:'CompanyWalletAvailable'}}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" machine={{id:'m',name:'Machine'}} getAccessToken={token}/>));
 const card=box.querySelector('[aria-label="Quota Compresseur"]')!;
 expect(card.textContent).toContain('Quota mensuel non actif');expect(card.textContent).not.toContain('Disponible');
 expect(card.querySelector('progress')?.classList.contains(styles.quotaInactive)).toBe(true);
 expect(card.textContent?.replace(/\s/g,' ')).toContain('0 % disponible');
 expect(box.querySelector('[aria-label="Crédit supplémentaire"]')?.textContent).toContain('20,00');
});
beforeEach(()=>{request.mockReset();localStorage.clear();box=document.createElement('div');document.body.append(box);root=createRoot(box);});
afterEach(async()=>{await act(async()=>root.unmount());box.remove();localStorage.clear();vi.unstubAllGlobals();});
it('shows only client financial information and preserves the recharge key after a lost response',async()=>{
 const assign=vi.fn();vi.stubGlobal('location',{assign});
 request.mockImplementation((_:unknown,path:string,body:unknown)=>Promise.resolve(body?{kind:'error'}:{kind:'success',data:path==='/topups'?[]:summary}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" machine={{id:'m',name:'Compresseur'}} getAccessToken={token}/>));
 expect(box.textContent).toContain('Compresseur');expect(box.textContent).toContain('Paiement en retard');expect(box.textContent).toContain('Facture impayée');expect(box.textContent).toContain('Crédit IA épuisé');
 expect(box.textContent).not.toMatch(/tokens|modèle|ledger|Stripe|fournisseur/i);
 expect(box.querySelector('progress')?.value).toBe(0);
 expect(box.textContent).not.toContain('100 %');expect(box.querySelector('[aria-label="Quota Compresseur"]')?.textContent).not.toContain('10,00');
 expect(box.textContent).toContain('29,90');expect(box.textContent).toContain('Aucune période active');
 const button=()=>Array.from(box.querySelectorAll('button')).find(b=>b.textContent==='Ajouter du crédit supplémentaire'||b.textContent==='Réessayer la recharge')!;
 await act(async()=>button().click());const first=request.mock.calls.find(c=>c[2])![2];
 request.mockImplementation((_:unknown,path:string,body:unknown)=>Promise.resolve(body?{kind:'success',data:{id:first.requestId,amount:20,status:'Pending',paymentUrl:'https://checkout.stripe.com/test'}}:{kind:'success',data:path==='/topups'?[]:summary}));
 await act(async()=>button().click());expect(request.mock.calls.filter(c=>c[2])[1][2]).toEqual(first);
 expect(assign).toHaveBeenCalledExactlyOnceWith('https://checkout.stripe.com/test');expect(box.querySelector('a')).toBeNull();expect(box.textContent).not.toContain(first.requestId);
});
it('does not open an unsafe payment URL and handles no selected machine',async()=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[{id:'op',amount:10,status:'Pending',paymentUrl:'https://evil.example'}]:{...summary,machineCreditRemaining:null,rechargeEnabled:false}}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" getAccessToken={token}/>));
 expect(box.textContent).toContain('Machines et quotas');expect(box.querySelector('a')).toBeNull();expect(box.textContent).toContain('temporairement indisponible');
 expect(box.querySelector('progress')?.value).toBe(0);
});
it('shows partial consumption without confusing reset and subscription dates',async()=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[]:{...summary,machineCreditRemaining:7.5,creditStatus:null,machineCreditResetUtc:'2026-10-14T23:28:51Z'}}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" machine={{id:'m',name:'Machine'}} getAccessToken={token}/>));
 expect(box.querySelector('progress')?.value).toBe(75);expect(box.textContent).toContain('75 %');
 expect(box.textContent).toContain('Réinitialisation le 14 octobre 2026');expect(box.querySelector('time')?.dateTime).toBe('2026-10-14T23:28:51Z');expect(box.querySelector('[role="alert"]')).toBeNull();
});


it('hides unpaid operations and allows a new recharge without changing the confirmed balance',async()=>{
 const assign=vi.fn();vi.stubGlobal('location',{assign});
 request.mockImplementation((_:unknown,path:string,body:unknown)=>Promise.resolve({kind:'success',data:body?{id:'new',amount:20,status:'AwaitingPayment',paymentUrl:'https://checkout.stripe.com/new'}:path==='/topups'?[{id:'old',amount:10,status:'AwaitingPayment',paymentUrl:'https://checkout.stripe.com/old'}]:summary}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" getAccessToken={token}/>));
 const card=box.querySelector('[aria-label="Crédit supplémentaire"]')!;
 expect(card.textContent).toContain('20,00');expect(card.textContent).not.toContain('en attente');expect(card.textContent).not.toContain('Payer la recharge');expect(card.querySelector('a')).toBeNull();
 await act(async()=>[...card.querySelectorAll('button')].find(b=>b.textContent==='Ajouter du crédit supplémentaire')!.click());
 expect(assign).toHaveBeenCalledExactlyOnceWith('https://checkout.stripe.com/new');
 expect(request.mock.calls.find(c=>c[2])![2].requestId).not.toBe('old');
 expect(card.textContent).toContain('20,00');expect(card.textContent).not.toContain('en attente');
});

it.each([[10,'quotaHigh'],[5,'quotaHigh'],[4.9,'quotaMedium'],[2,'quotaMedium'],[1.9,'quotaLow'],[0.1,'quotaLow'],[0,'quotaInactive']])('colors available quota at remaining %s without changing its value',async(remaining,color)=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[]:{...summary,machineCreditRemaining:remaining,machineCreditResetUtc:'2026-10-14T00:00:00Z'}}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" getAccessToken={token}/>));
 const progress=box.querySelector('[aria-label="Quota Compresseur"] progress') as HTMLProgressElement;
 expect(progress.value).toBe(Number(remaining)*10);expect(progress.classList.contains(styles[color as string])).toBe(true);
 expect(progress.getAttribute('aria-label')).toContain('Quota disponible');
 expect(box.textContent).toContain('Utilisé automatiquement quand le quota inclus de la machine est épuisé.');
});
