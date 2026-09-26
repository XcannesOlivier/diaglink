import { useState } from 'react';
import { Button } from '@fluentui/react-components';
import { setStripeMachineStatus, type StripeCompanySummary } from '../../services/stripeAdminService';
const date = (value: string | null) => value
  ? new Date(value.endsWith('Z') ? value : `${value}Z`).toLocaleDateString('fr-FR', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' })
  : 'Aucune période';
export function StripeMachineStatusPanel({companyId,account,token,refresh}:{companyId:string;account:StripeCompanySummary;token:()=>Promise<string|null>;refresh:()=>void}) {
  const [busy,setBusy]=useState(false); const [message,setMessage]=useState('');
  async function change(machineId:string,active:boolean) {
    if(busy)return;
    const key=`diaglink:machine-status:${companyId}:${machineId}`;
    let pending: {active:boolean;id:string};
    try {
      pending=JSON.parse(localStorage.getItem(key)||'null') ?? {active,id:crypto.randomUUID()};
      localStorage.setItem(key,JSON.stringify(pending));
    } catch {setMessage('Stockage indisponible : impossible de sécuriser la reprise.');return;}
    setBusy(true);
    const result=await setStripeMachineStatus(token,companyId,machineId,pending.active,pending.id);
    setBusy(false);
    if(result.kind==='success'){localStorage.removeItem(key);setMessage('Modification enregistrée.');refresh();}
    else setMessage('Modification non confirmée. Réessayez pour reprendre la même demande.');
  }
  return <section aria-label="Gestion de la facturation des machines">
    <h4>Gestion de la facturation des machines</h4>
    <p>La désactivation réduit la quantité du prochain renouvellement ; les droits déjà payés sont conservés jusqu’à leur fin.</p>
    {account.machines?.map(m=><div key={m.id}>
      <p>{m.name} · {m.billable?'Facturable':'Non renouvelée'} · Fin des droits payés : {date(m.rightsEndUtc)}</p>
      <Button disabled={busy || !account.testActionsEnabled} onClick={()=>void change(m.id,!m.billable)}>{m.billable?'Désactiver':'Réactiver'} {m.name}</Button>
    </div>)}<p role="status">{message}</p>
  </section>;
}
