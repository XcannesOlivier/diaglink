import {type ReactNode,type SyntheticEvent,useCallback,useState} from 'react';
import {CompanyConsumptionPanel} from './CompanyConsumptionPanel';
import {type AdminAccordionControl,type AdminSection} from './adminAccordion';
import styles from './CompanyFinancePanel.module.css';
import type {MachineFinanceIntervention} from './financeInterventions';
export function AdminFinanceOverview({companyId,token,revision=0,globalContent,accordion,onConsumptionLoadComplete,machineInterventions,onExamineInterventions,machineActionsEnabled,onMachineSubscriptionChanged,onDiagLinkSessionExpired}:{companyId:string;token:()=>Promise<string|null>;revision?:number;globalContent?:ReactNode;accordion?:AdminAccordionControl;onConsumptionLoadComplete?:(view:'global'|'machines')=>void;machineInterventions?:MachineFinanceIntervention[];onExamineInterventions?:()=>void;machineActionsEnabled?:boolean;onMachineSubscriptionChanged?:()=>void;onDiagLinkSessionExpired?:()=>void}){
 const globalLoadComplete=useCallback(()=>onConsumptionLoadComplete?.('global'),[onConsumptionLoadComplete]);
 const machinesLoadComplete=useCallback(()=>onConsumptionLoadComplete?.('machines'),[onConsumptionLoadComplete]);
 const [uncontrolledSection,setUncontrolledSection]=useState<AdminSection|null>(null);
 const openSection=accordion?.openSection??uncontrolledSection;
 const toggle=(section:AdminSection)=>(event:SyntheticEvent<HTMLDetailsElement>)=>{
  if(event.target!==event.currentTarget)return;
  const isOpen=event.currentTarget.open;
  if(accordion)accordion.onSectionToggle(section,isOpen);
  else setUncontrolledSection(current=>isOpen?section:current===section?null:current);
 };
  return <section aria-label="Synthèse financière entreprise" className={`${styles.panel} ${styles.adminFinanceRoot}`}>
  <details className={`${styles.technical} ${styles.primarySection}`} open={openSection==='globalConsumption'} onToggle={toggle('globalConsumption')}><summary>Consommation globale</summary>
  <CompanyConsumptionPanel key={`global-${companyId}`} companyId={companyId} token={token} revision={revision} view="global" active={openSection==='globalConsumption'} onLoadComplete={globalLoadComplete} globalContent={globalContent}/>
  </details>
  <details className={`${styles.technical} ${styles.primarySection}`} open={openSection==='machineConsumption'} onToggle={toggle('machineConsumption')}><summary>Consommation par machine</summary>
  <CompanyConsumptionPanel key={`machines-${companyId}`} companyId={companyId} token={token} revision={revision} view="machines" active={openSection==='machineConsumption'} onLoadComplete={machinesLoadComplete} machineInterventions={machineInterventions} onExamineInterventions={onExamineInterventions} machineActionsEnabled={machineActionsEnabled} onMachineSubscriptionChanged={onMachineSubscriptionChanged} onDiagLinkSessionExpired={onDiagLinkSessionExpired}/>
  </details>
 </section>;
}
