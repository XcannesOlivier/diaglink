import {type ReactNode,useCallback} from 'react';
import {CompanyConsumptionPanel} from './CompanyConsumptionPanel';
import {getAdminAccordionProps, type AdminAccordionControl} from './adminAccordion';
import styles from './CompanyFinancePanel.module.css';
import type {MachineFinanceIntervention} from './financeInterventions';
export function AdminFinanceOverview({companyId,token,revision=0,globalContent,accordion,onConsumptionLoadComplete,machineInterventions,onExamineInterventions,machineActionsEnabled,onMachineSubscriptionChanged,onDiagLinkSessionExpired}:{companyId:string;token:()=>Promise<string|null>;revision?:number;globalContent?:ReactNode;accordion?:AdminAccordionControl;onConsumptionLoadComplete?:(view:'global'|'machines')=>void;machineInterventions?:MachineFinanceIntervention[];onExamineInterventions?:()=>void;machineActionsEnabled?:boolean;onMachineSubscriptionChanged?:()=>void;onDiagLinkSessionExpired?:()=>void}){
 const globalLoadComplete=useCallback(()=>onConsumptionLoadComplete?.('global'),[onConsumptionLoadComplete]);
 const machinesLoadComplete=useCallback(()=>onConsumptionLoadComplete?.('machines'),[onConsumptionLoadComplete]);
  return <section aria-label="Synthèse financière entreprise" className={`${styles.panel} ${styles.adminFinanceRoot}`}>
  <details className={`${styles.technical} ${styles.primarySection}`} {...getAdminAccordionProps(accordion,'globalConsumption')}><summary>Consommation globale</summary>
  <CompanyConsumptionPanel key={`global-${companyId}`} companyId={companyId} token={token} revision={revision} view="global" onLoadComplete={globalLoadComplete} globalContent={globalContent}/>
  </details>
  <details className={`${styles.technical} ${styles.primarySection}`} {...getAdminAccordionProps(accordion,'machineConsumption')}><summary>Consommation par machine</summary>
  <CompanyConsumptionPanel key={`machines-${companyId}`} companyId={companyId} token={token} revision={revision} view="machines" onLoadComplete={machinesLoadComplete} machineInterventions={machineInterventions} onExamineInterventions={onExamineInterventions} machineActionsEnabled={machineActionsEnabled} onMachineSubscriptionChanged={onMachineSubscriptionChanged} onDiagLinkSessionExpired={onDiagLinkSessionExpired}/>
  </details>
 </section>;
}
