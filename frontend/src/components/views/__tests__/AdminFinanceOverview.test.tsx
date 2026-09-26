import {it,expect,vi} from 'vitest';
import {act} from 'react';import {createRoot} from 'react-dom/client';
import {AdminFinanceOverview} from '../AdminFinanceOverview';import type {StripeCompanySummary} from '../../../services/stripeAdminService';
vi.mock('../CompanyConsumptionPanel',()=>({CompanyConsumptionPanel:()=><section aria-label="Suivi de consommation"/>}));
it('keeps consumption and payments folded while billing state stays visible',async()=>{
 const host=document.createElement('div'),root=createRoot(host);
 try{await act(async()=>root.render(<AdminFinanceOverview companyId="c" token={async()=>null} account={{activeMachineCount:1} as StripeCompanySummary} machineContent={<span>Recharge</span>}><span>Diagnostic</span></AdminFinanceOverview>));
 expect([...host.querySelectorAll('summary')].map(s=>s.textContent)).toEqual(['Consommation Agent','Crédits et paiements']);
 expect([...host.querySelectorAll('details')][1].textContent).toContain('Recharge');
 expect(host.querySelector('[aria-label="État de facturation"]')?.textContent).toContain('Diagnostic');
 expect(host.textContent).not.toContain('Machines facturées');
 expect(host.textContent).not.toContain('Montant impayé');
 expect(host.textContent).not.toContain('Statut et consommation des machines');
 }finally{await act(async()=>root.unmount());}
});
