import {it,expect,vi} from 'vitest';
import {act} from 'react';import {createRoot} from 'react-dom/client';
import {AdminFinanceOverview} from '../AdminFinanceOverview';import type {StripeCompanySummary} from '../../../services/stripeAdminService';
vi.mock('../CompanyConsumptionPanel',()=>({CompanyConsumptionPanel:()=><section aria-label="Suivi de consommation"/>}));
it('separates consumption, payments and diagnostics',async()=>{
 const host=document.createElement('div'),root=createRoot(host);
 try{await act(async()=>root.render(<AdminFinanceOverview companyId="c" token={async()=>null} account={{activeMachineCount:1} as StripeCompanySummary} machineContent={<span>Recharge</span>}><span>Diagnostic</span></AdminFinanceOverview>));
 expect([...host.querySelectorAll('summary')].map(s=>s.textContent)).toEqual(['Consommation Agent','Crédits et paiements','Diagnostic Super Admin']);
 expect([...host.querySelectorAll('details')][1].textContent).toContain('Recharge');
 expect([...host.querySelectorAll('details')][2].textContent).toContain('Diagnostic');
 expect([...host.querySelectorAll('details')][2].open).toBe(false);
 expect(host.textContent).not.toContain('Machines facturées');
 expect(host.textContent).not.toContain('Montant impayé');
 expect(host.textContent).not.toContain('Statut et consommation des machines');
 }finally{await act(async()=>root.unmount());}
});
