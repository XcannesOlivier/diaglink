import {it,expect,vi,afterEach} from 'vitest';
import {act} from 'react';
import {createRoot} from 'react-dom/client';
import {CompanySummaryBanner} from '../CompanySummaryBanner';
import type {StripeCompanySummary} from '../../../services/stripeAdminService';
vi.mock('../../../utils/apiAuth',()=>({getApiAuthHeaders:async()=>({headers:{}})}));
vi.mock('../../../services/userService',()=>({getUsersByCompany:async()=>({kind:'success',data:[{id:'u'}]})}));
vi.mock('../../../services/aiUsageService',()=>({getUsageCompanies:async()=>({kind:'success',data:[{companyId:'a',metrics:{eventCount:12}},{companyId:'b',metrics:{eventCount:999}}]})}));
afterEach(()=>vi.unstubAllGlobals());
it('displays the three useful metrics without inventing a credit ceiling',async()=>{
 const fetch=vi.fn().mockResolvedValue(new Response(JSON.stringify({walletBalance:20})));
 vi.stubGlobal('fetch',fetch);const host=document.createElement('div'),root=createRoot(host);
 try{await act(async()=>root.render(<CompanySummaryBanner companyId="a" account={{subscriptionStatus:'active',activeMachineCount:2,latestInvoiceStatus:'open',amountRemainingCents:2990} as StripeCompanySummary} token={async()=>null} revision={0}/>));
 expect(host.querySelectorAll('dt')).toHaveLength(3);
 expect([...host.querySelectorAll('dd')].map(e=>e.textContent?.replace(/\s/g,' '))).toEqual(['2','20,00 €','Abonnement actifMontant impayé29,90 €']);
 expect([...host.querySelectorAll('dt')].map(e=>e.textContent)).toEqual(['Machines facturables','Crédit supplémentaire','Abonnement']);
 expect(host.querySelector('dd [aria-hidden="true"]')).toBeNull();
 expect(host.textContent).not.toContain('Usages IA du mois');
 expect(host.textContent).not.toContain('Wallet entreprise');
 expect(host.textContent).not.toContain('999');expect(fetch.mock.calls[0][0]).toContain('/companies/a/stripe/finance');
 }finally{await act(async()=>root.unmount());}
});

it.each([
 ['active','paid',0,'Abonnement payé',false],
 ['active','open',0,'Abonnement payé',false],
 ['active','open',2990,'Abonnement actif',true],
 ['past_due','open',2990,'Paiement en retard',true],
 ['unpaid','open',2990,'Paiement en retard',true],
 ['incomplete','open',2990,'Paiement initial en attente',true],
 [null,null,null,'Aucun abonnement',false],
 ['canceled','paid',0,'Résilié',false]
])('shows subscription state %s without inventing payment or amounts',async(subscriptionStatus,latestInvoiceStatus,amountRemainingCents,label,unpaid)=>{
 vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response(JSON.stringify({walletBalance:0}))));
 const host=document.createElement('div'),root=createRoot(host);
 try{await act(async()=>root.render(<CompanySummaryBanner companyId="a" account={{subscriptionStatus,latestInvoiceStatus,amountRemainingCents} as StripeCompanySummary} token={async()=>null} revision={0}/>));
 const cell=host.querySelectorAll('dd')[2];expect(cell.textContent).toContain(label);
 expect(cell.textContent?.includes('Montant impayé')).toBe(unpaid);
 if(unpaid)expect(cell.textContent).toContain(new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(29.9));
 else expect(cell.textContent).not.toContain('€');
 }finally{await act(async()=>root.unmount());}
});
