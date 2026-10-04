import styles from './CompanyFinancePanel.module.css';

export interface MachineConsumptionUserRow {
  id: string | null;
  name: string;
  includedQuotaConsumed: number;
  commercialCredit: number;
}

const euro=(value:number)=>new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(value);
const quotaShare=(consumed:number,budget:number)=>`${new Intl.NumberFormat('fr-FR',{maximumFractionDigits:1}).format(budget>0?consumed/budget*100:0)} %`;

export function MachineUserConsumption({machineName,includedQuotaBudget,users,showIncludedQuotaConsumed=true}:{machineName:string;includedQuotaBudget:number;users:MachineConsumptionUserRow[];showIncludedQuotaConsumed?:boolean}) {
  return <div className={styles.machineUsers}>
    <div className={`${styles.compactTableScroll} ${styles.consumptionDesktop}`}>
      <table className={styles.compactTable}><caption>Consommation par utilisateur — {machineName}</caption><thead><tr>{['Utilisateur',...(showIncludedQuotaConsumed?['Quota inclus consommé']:[]),'Part du quota inclus','Crédit supplémentaire consommé'].map(label=><th scope="col" key={label}>{label}</th>)}</tr></thead><tbody>{users.map((user,index)=><tr key={`${user.id??'unassigned'}-${index}`}><td>{user.name}</td>{showIncludedQuotaConsumed&&<td>{euro(user.includedQuotaConsumed)}</td>}<td>{quotaShare(user.includedQuotaConsumed,includedQuotaBudget)}</td><td>{euro(user.commercialCredit)}</td></tr>)}</tbody></table>
    </div>
    <div className={styles.consumptionMobile}>{users.map((user,index)=><article key={`${user.id??'unassigned'}-${index}`}><strong>{user.name}</strong><dl>{showIncludedQuotaConsumed&&<div><dt>Quota inclus consommé</dt><dd>{euro(user.includedQuotaConsumed)}</dd></div>}<div><dt>Part du quota inclus</dt><dd>{quotaShare(user.includedQuotaConsumed,includedQuotaBudget)}</dd></div><div><dt>Crédit supplémentaire consommé</dt><dd>{euro(user.commercialCredit)}</dd></div></dl></article>)}</div>
  </div>;
}
