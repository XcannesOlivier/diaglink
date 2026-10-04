import styles from './CompanyFinancePanel.module.css';
import { useCallback, useState } from 'react';
import { useApiResource } from '../../hooks/useApiResource';
import { getCompanies } from '../../services/companyService';
import { StripeCompanyPanel } from './StripeCompanyPanel';
import type { AdminAccordionControl } from './adminAccordion';

export function StripeAdminPanel({ getAccessToken, onDiagLinkSessionExpired, accordion, selectedCompanyId, onSelectedCompanyChange, refreshRevision=0, onRefreshComplete, onRefreshRequest }: {
  getAccessToken: () => Promise<string | null>; onDiagLinkSessionExpired?: () => void; accordion?: AdminAccordionControl;
  selectedCompanyId?: string; onSelectedCompanyChange?: (companyId:string)=>void; refreshRevision?:number; onRefreshComplete?:(revision:number)=>void; onRefreshRequest?:()=>void;
}) {
  const fetcher = useCallback(() => getCompanies(getAccessToken), [getAccessToken]);
  const state = useApiResource(fetcher, onDiagLinkSessionExpired);
  const [localSelected, setLocalSelected] = useState('');
  const selected=selectedCompanyId??localSelected;
  const setSelected=(value:string)=>{if(onSelectedCompanyChange)onSelectedCompanyChange(value);else setLocalSelected(value);};
  return <section className={styles.admin} aria-label="Administration par entreprise" style={{ gridColumn: '1 / -1', minWidth: 0 }}>
    <h2>Entreprise</h2>
    {state.kind === 'loading' && <p>Chargement des entreprises…</p>}
    {state.kind !== 'loading' && state.kind !== 'success' && <p role="alert">Entreprises indisponibles ou accès non autorisé.</p>}
    {state.kind === 'success' && <>
      <label><span className={styles.visuallyHidden}>Entreprise</span><select aria-label="Entreprise" value={selected} onChange={e => setSelected(e.target.value)}>
        <option value="">Sélectionner une entreprise</option>
        {state.data.map(c => <option key={c.id} value={c.id}>{c.name} ({c.status})</option>)}
      </select></label>
      {selected && state.data.some(c => c.id === selected) && <StripeCompanyPanel key={selected} companyId={selected}
        getAccessToken={getAccessToken} onDiagLinkSessionExpired={onDiagLinkSessionExpired} accordion={accordion}
        refreshRevision={refreshRevision} onRefreshComplete={onRefreshComplete} onRefreshRequest={onRefreshRequest} />}
    </>}
  </section>;
}
