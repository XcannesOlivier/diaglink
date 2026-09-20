import {it,expect,vi,afterEach} from 'vitest';
import {act} from 'react';import {createRoot} from 'react-dom/client';
import {DiagLinkAdminView} from '../DiagLinkAdminView';
const token=async()=>null;
vi.mock('../../../hooks/useAuth',()=>({useAuth:()=>({getAccessToken:token})}));
vi.mock('../../../utils/apiAuth',()=>({getApiAuthHeaders:async()=>({headers:{}})}));
vi.mock('../../../services/companyService',()=>({getCompanies:async()=>({kind:'success',data:[]})}));
vi.mock('../../../services/machineService',()=>({getMachines:vi.fn(async()=>({kind:'success',data:[]}))}));
vi.mock('../StripeAdminPanel',()=>({StripeAdminPanel:()=>null}));
vi.mock('../AiUsagePanel',()=>({AiUsagePanel:()=>null}));
afterEach(()=>vi.unstubAllGlobals());
it.each([0,1234.5])('shows cumulative EUR top-ups: %s',async(total)=>{
 const fetch=vi.fn().mockResolvedValue(new Response(JSON.stringify({companies:2,machines:3,users:4,subscriptionsPaidEur:0,includedCreditGrantedEur:0,topUpsAddedEur:total})));vi.stubGlobal('fetch',fetch);
 const host=document.createElement('div'),root=createRoot(host);
 try{await act(async()=>root.render(<DiagLinkAdminView/>));
 expect(fetch.mock.calls[0][0]).toContain('/admin/finance/period');
 expect(host.textContent).toContain('Crédit supplémentaire ajouté');expect(host.textContent).not.toContain('Recharges confirmées et créditées sur la période');
 expect(host.textContent).toContain(new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(total));
 expect(host.textContent).not.toContain('Documents PDF');
 expect(host.textContent).not.toContain('Consommation globale de la plateforme');
 expect(host.querySelectorAll('article')).toHaveLength(6);
 }finally{await act(async()=>root.unmount());}
});
