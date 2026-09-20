import {type ReactNode} from 'react';
import {CompanyConsumptionPanel} from './CompanyConsumptionPanel';
import type {StripeCompanySummary} from '../../services/stripeAdminService';
import styles from './CompanyFinancePanel.module.css';
const date=(s:string|null)=>s?new Date(s.endsWith('Z')?s:`${s}Z`).toLocaleDateString('fr-FR',{day:'numeric',month:'long',year:'numeric',timeZone:'UTC'}):'Non disponible';
export function AdminFinanceOverview({companyId,account,token,revision=0,children,machineContent,diagnosticTarget,costsTarget}:{companyId:string;account:StripeCompanySummary;token:()=>Promise<string|null>;revision?:number;children?:ReactNode;machineContent?:ReactNode;diagnosticTarget?:HTMLElement|null;costsTarget?:HTMLElement|null}){
 void companyId; void diagnosticTarget;
 return <section aria-label="Synthèse financière entreprise" className={`${styles.panel} ${styles.adminFinanceRoot}`}>
  <details className={`${styles.technical} ${styles.primarySection}`}><summary>Consommation Agent</summary>
   <CompanyConsumptionPanel key={companyId} companyId={companyId} token={token} revision={revision} diagnosticTarget={costsTarget}/>
  </details>
   <details className={styles.technical}><summary>Crédits et paiements</summary>
   <p><strong>Prochaine échéance : </strong>{account.cancelAtPeriodEnd||account.subscriptionStatus==='canceled'?'Aucune prévue':date(account.currentPeriodEndUtc)}{account.cancelAtPeriodEnd&&' · Résiliation prévue à échéance'}</p>
   {machineContent}
   </details>
   <details className={styles.technical}><summary>Diagnostic Super Admin</summary>{children}</details>
 </section>;
}
