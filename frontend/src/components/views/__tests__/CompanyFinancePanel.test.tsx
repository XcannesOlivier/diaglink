import styles from '../CompanyFinancePanel.module.css';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
﻿import {beforeEach,afterEach,it,expect,vi} from 'vitest';
import {act} from 'react';import {createRoot,type Root} from 'react-dom/client';
import {CompanyFinancePanel} from '../CompanyFinancePanel';
const {request,consumptionRequest,setCompanyStatus}=vi.hoisted(()=>({request:vi.fn(),consumptionRequest:vi.fn(),setCompanyStatus:vi.fn()}));
vi.mock('../../../services/companyFinanceService',()=>({financeRequest:request,companyConsumptionRequest:consumptionRequest}));
vi.mock('../../../services/stripeAdminService',()=>({setCompanyStripeMachineStatus:setCompanyStatus,setStripeMachineStatus:vi.fn()}));
vi.mock('../../../services/machineService',()=>({getMachines:async()=>({kind:'success',data:[{id:'m',name:'Compresseur',companyId:'c',status:'active'},{id:'i',name:'Inactive',companyId:'c',status:'inactive'},{id:'other',name:'Foreign',companyId:'other',status:'active'}]})}));
let box:HTMLDivElement,root:Root;
const token=async()=>null;
const summary={machineCreditResetUtc:null,machineCreditRemaining:0,walletBalance:20,subscriptionStatus:'past_due',nextDueUtc:'2026-11-14T18:28:51Z',cancelAtPeriodEnd:false,unpaidInvoiceAmount:29.90,rechargeEnabled:true,creditStatus:'AiCreditExhausted'};
const consumption={machines:[
 {id:'m',name:'Compresseur',billable:true,hasPaidRights:true,includedQuotaBudget:10,resetUtc:'2026-10-14T00:00:00Z',commercialCredit:.04,users:[{id:'u1',name:'Alice',includedQuotaConsumed:1,commercialCredit:.02}]},
 {id:'i',name:'Inactive',billable:false,hasPaidRights:false,includedQuotaBudget:0,resetUtc:null,commercialCredit:0,users:[]},
]};
it('keeps the company machine search at its intrinsic height on mobile',()=>{
 const css=readFileSync(resolve('src/components/views/CompanyFinancePanel.module.css'),'utf8');
 const mobileRule='@media(max-width:767px){.companyMachineControls{align-items:stretch;flex-direction:column;gap:12px}.companyMachineControls label,.companyMachineControls select,.companyMachineControls .machineSearch{width:100%;max-width:none}.companyMachineControls .machineSearch{flex:0 0 auto}}';
 expect(css).toContain(mobileRule);
 expect(mobileRule).not.toContain('height:');
 expect(mobileRule).not.toContain('!important');
});
it.each([false,true])('shows cancellation only when scheduled: %s',async(cancelAtPeriodEnd)=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[]:{...summary,cancelAtPeriodEnd}}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" getAccessToken={token}/>));
 const subscription=box.querySelector<HTMLDetailsElement>('[aria-label="Abonnement"]')!;
 const rightColumn=subscription.parentElement!;
 expect(rightColumn.classList.contains(styles.companyFinanceRightColumn)).toBe(true);
 expect(rightColumn.firstElementChild?.getAttribute('aria-label')).toBe('Crédit supplémentaire');
 expect(rightColumn.querySelectorAll('[aria-label="Abonnement"]')).toHaveLength(1);
 expect(box.querySelector('[aria-label="Machines et quotas"]')?.contains(subscription)).toBe(false);
 expect(subscription.open).toBe(false);
 await act(async()=>subscription.querySelector('summary')!.click());expect(subscription.open).toBe(true);
 if(cancelAtPeriodEnd)expect(subscription.textContent).toContain('Résiliation prévue à l’échéance');
 else expect(subscription.textContent).not.toContain('Résiliation prévue');
 expect([...subscription.querySelectorAll('dd')].map(e=>e.textContent)).not.toContain('Non');
 const inactive=box.querySelector('[aria-label="Quota Inactive"]')!;
 expect(inactive.querySelector('time')).toBeNull();expect(inactive.textContent).not.toContain('Réinitialisation');
 expect(box.querySelector('[aria-label="Quota Compresseur"]')?.textContent).toContain('Quota mensuel : Non actif');
});
it('lists all company machines with independent quotas regardless of chat selection',async()=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[]:{...summary,machineCreditRemaining:path==='?machineId=m'?6.4:0,machineCreditResetUtc:path==='?machineId=m'?'2026-10-14T00:00:00Z':null}}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" machine={{id:'other',name:'Chat selection'}} getAccessToken={token}/>));
 expect(box.querySelector('[aria-label="Quota Compresseur"]')?.textContent).toContain('64 % disponible');
 expect(box.querySelector('[aria-label="Quota Inactive"]')?.textContent).toContain('Abonnement : Inactif');
 expect(box.querySelector('[aria-label="Quota Inactive"]')?.textContent).toContain('Quota mensuel : Non actif');
 expect(box.textContent).not.toContain('Foreign');expect(box.textContent).not.toContain('Chat selection');
 expect(request).not.toHaveBeenCalledWith(token,'?machineId=other');
 expect(box.textContent).toContain('Nombre total de machines');expect(box.querySelectorAll('li[aria-label^="Quota "]')).toHaveLength(2);
});
it('présente les machines en cartes alternées avec des sous-cartes neutres',async()=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[]:{...summary,machineCreditRemaining:6.4,machineCreditResetUtc:'2026-10-14T00:00:00Z'}}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" getAccessToken={token}/>));
 const cards=[...box.querySelectorAll<HTMLElement>('[data-machine-card]')];
 expect(cards).toHaveLength(2);
 expect(cards.every(card=>card.classList.contains(styles.machineConsumptionCard))).toBe(true);
 expect(cards[0].classList.contains(styles.machineConsumptionCardAlternate)).toBe(true);
 expect(cards[1].classList.contains(styles.machineConsumptionCardAlternate)).toBe(false);
 expect([...cards[0].querySelectorAll('article')].map(card=>card.getAttribute('aria-label'))).toEqual(['Quota inclus de Compresseur','Crédit supplémentaire consommé de Compresseur','État de Compresseur']);
 expect(cards[0].querySelector('[aria-label="Quota inclus de Compresseur"]')?.classList.contains(styles.companyQuotaMetricCard)).toBe(true);
 expect(cards[0].querySelector('[aria-label="État de Compresseur"]')?.classList.contains(styles.companyQuotaMetricCard)).toBe(true);
 expect([...cards[0].querySelectorAll('article')].some(card=>card.classList.contains(styles.quotaHigh))).toBe(false);
 expect(cards[0].textContent).toContain('64 % disponible');
 expect(cards[0].querySelector('[aria-label="Quota inclus de Compresseur"]')?.textContent).not.toMatch(/€|EUR/);
 expect(cards[0].querySelector('[aria-label="Crédit supplémentaire consommé de Compresseur"]')?.textContent).toContain('0,04');
 expect(box.querySelector('[aria-label="Crédit supplémentaire"]')?.textContent).toContain('20,00');
});
it('actualise les métriques temporelles et ouvre le détail utilisateur de la bonne machine',async()=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[]:{...summary,machineCreditRemaining:6.4,machineCreditResetUtc:'2026-10-14T00:00:00Z'}}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" getAccessToken={token}/>));
 const select=box.querySelector<HTMLSelectElement>('[aria-label="Période de consommation des machines"]')!;
 expect(select.value).toBe('month');
 await act(async()=>{select.value='week';select.dispatchEvent(new Event('change',{bubbles:true}));});
 expect(consumptionRequest).toHaveBeenLastCalledWith(token,expect.objectContaining({from:expect.any(String),to:expect.any(String)}));
 const card=box.querySelector<HTMLElement>('[data-machine-card="Compresseur"]')!;
 const toggle=[...card.querySelectorAll<HTMLButtonElement>('button')].find(button=>button.textContent==='Voir la consommation par utilisateur')!;
 await act(async()=>toggle.click());
 expect(toggle.textContent).toBe('Masquer la consommation par utilisateur');
 const table=card.querySelector('table')!;
 expect([...table.querySelectorAll('thead th')].map(header=>header.textContent)).toEqual(['Utilisateur','Part du quota inclus','Crédit supplémentaire consommé']);
 expect(card.textContent).not.toContain('Quota inclus consommé');
 expect(table.querySelector('tbody tr')?.textContent).toContain('Alice');
 expect(table.querySelector('tbody tr')?.textContent).toContain('10 %');
 expect(table.querySelector('tbody tr')?.textContent).toContain('0,02');
 expect(box.querySelector<HTMLElement>('[data-machine-card="Inactive"]')?.querySelector('table')).toBeNull();
});
it('ouvre Gérer sur la bonne machine et rafraîchit depuis le serveur après succès',async()=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[]:{...summary,machineCreditRemaining:6.4,machineCreditResetUtc:'2026-10-14T00:00:00Z'}}));
 setCompanyStatus.mockResolvedValue({kind:'success',data:{status:'Synchronized'}});
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" getAccessToken={token}/>));
 const card=box.querySelector<HTMLElement>('[data-machine-card="Compresseur"]')!;
 const manage=[...card.querySelectorAll<HTMLButtonElement>('button')].find(button=>button.textContent==='Gérer')!;
 expect(card.querySelector('header div')?.textContent).toBe('CompresseurGérer');
 await act(async()=>manage.click());
 expect(document.querySelector('[role="dialog"]')?.textContent).toContain('Gérer la machine — Compresseur');
 await act(async()=>[...document.querySelectorAll<HTMLButtonElement>('button')].find(button=>button.textContent==='Résilier l’abonnement')!.click());
 await act(async()=>[...document.querySelectorAll<HTMLButtonElement>('button')].find(button=>button.textContent==='Confirmer la résiliation')!.click());
 expect(setCompanyStatus).toHaveBeenCalledWith(token,'m',false,expect.any(String));
 expect(consumptionRequest.mock.calls.length).toBeGreaterThan(1);
 expect(box.textContent).toContain('Période');
 expect(box.querySelector('[aria-label="Rechercher une machine"]')).not.toBeNull();
});
it.each([[10,100],[6,60],[6.4,64],[0.001,0],[0,0]])('shows monthly credit only as percentages for remaining %s',async(remaining,available)=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[]:{...summary,machineCreditRemaining:remaining,machineCreditResetUtc:'2026-10-14T18:28:51Z'}}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" machine={{id:'m',name:'Machine'}} getAccessToken={token}/>));
 const card=box.querySelector('[aria-label="Quota Compresseur"]')!;
 expect(card.textContent).toContain(`${available} % disponible`);
 expect(card.textContent).not.toContain('Quota mensuel : Actif');
 if(remaining<=0)expect(card.textContent).toContain('Quota mensuel : Épuisé');
 else expect(card.textContent).not.toContain('Quota mensuel :');
 expect(card.querySelector('[aria-label="Quota inclus de Compresseur"]')?.textContent).not.toMatch(/€|EUR|10,00/);
 expect(card.querySelector('progress')?.value).toBe(available);
 const inactive=box.querySelector('[aria-label="Quota Inactive"]')!;
 expect(inactive.textContent).toContain('Abonnement : Inactif');
 expect(inactive.textContent).not.toContain('Accès maintenu jusqu’au');
 expect(inactive.textContent).not.toContain('Réinitialisation');
 expect(inactive.querySelector('time')).toBeNull();
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
 expect(card.textContent).toContain('Quota mensuel : Non actif');expect(card.textContent).not.toContain('Disponible');
 expect(card.querySelector('progress')?.classList.contains(styles.quotaInactive)).toBe(true);
 expect(card.textContent?.replace(/\s/g,' ')).toContain('0 % disponible');
 expect(box.querySelector('[aria-label="Crédit supplémentaire"]')?.textContent).toContain('20,00');
});
beforeEach(()=>{request.mockReset();consumptionRequest.mockReset();setCompanyStatus.mockReset();consumptionRequest.mockResolvedValue({kind:'success',data:consumption});localStorage.clear();box=document.createElement('div');document.body.append(box);root=createRoot(box);});
afterEach(async()=>{await act(async()=>root.unmount());box.remove();localStorage.clear();vi.unstubAllGlobals();});
it('shows only client financial information and preserves the recharge key after a lost response',async()=>{
 const assign=vi.fn();vi.stubGlobal('location',{assign});
 request.mockImplementation((_:unknown,path:string,body:unknown)=>Promise.resolve(body?{kind:'error'}:{kind:'success',data:path==='/topups'?[]:summary}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" machine={{id:'m',name:'Compresseur'}} getAccessToken={token}/>));
 expect(box.textContent).toContain('Compresseur');expect(box.textContent).toContain('Paiement en retard');expect(box.textContent).toContain('Facture impayée');expect(box.textContent).toContain('Crédit IA épuisé');
 expect(box.textContent).not.toMatch(/tokens|modèle|ledger|Stripe|fournisseur/i);
 expect(box.querySelector('progress')?.value).toBe(0);
 expect(box.textContent).not.toContain('100 %');expect(box.querySelector('[aria-label="Quota Compresseur"]')?.textContent).not.toContain('10,00');
 expect(box.textContent).toContain('29,90');expect(box.textContent).toContain('Quota mensuel : Non actif');
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
it('shows partial consumption without displaying the quota reset date',async()=>{
 request.mockImplementation((_:unknown,path:string)=>Promise.resolve({kind:'success',data:path==='/topups'?[]:{...summary,machineCreditRemaining:7.5,creditStatus:null,machineCreditResetUtc:'2026-10-14T23:28:51Z'}}));
 await act(async()=>root.render(<CompanyFinancePanel companyId="c" machine={{id:'m',name:'Machine'}} getAccessToken={token}/>));
 expect(box.querySelector('progress')?.value).toBe(75);expect(box.textContent).toContain('75 %');
 expect(box.textContent).not.toContain('Réinitialisation');expect(box.textContent).not.toContain('14 octobre 2026');expect(box.querySelector('time')).toBeNull();expect(box.querySelector('[role="alert"]')).toBeNull();
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
