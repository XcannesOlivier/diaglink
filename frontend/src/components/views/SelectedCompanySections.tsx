import {useEffect,useState} from 'react';
import {getUsersByCompany} from '../../services/userService';
import type {CompanyUserDto} from '../../types/company';
import styles from './CompanyFinancePanel.module.css';

export function SelectedCompanySections({companyId,token}:{companyId:string;token:()=>Promise<string|null>}) {
 const [users,setUsers]=useState<CompanyUserDto[]|null>(null);
 const [usersError,setUsersError]=useState(false);
 useEffect(()=>{let active=true;setUsers(null);setUsersError(false);
  getUsersByCompany(token,companyId).then(r=>{if(active){if(r.kind==='success')setUsers(r.data);else setUsersError(true);}});
  return()=>{active=false;};
 },[companyId,token]);
 return <>
  <h3>Utilisateurs</h3>
  {usersError?<p role="alert">Utilisateurs indisponibles.</p>:users===null?<p>Chargement…</p>:users.length===0?<p>Aucun utilisateur.</p>:<div className={styles.compactTableScroll}><table className={styles.compactTable}>
  <thead><tr><th>Nom</th><th>Email</th><th>Rôle</th><th>État</th></tr></thead>
  <tbody>{users.map(u=><tr key={u.id}><td>{[u.firstName,u.lastName].filter(Boolean).join(' ')||u.email}</td><td>{u.email}</td><td>{u.role}</td><td>{u.status}</td></tr>)}</tbody>
  </table></div>}
 </>;
}
