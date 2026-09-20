import {it,expect,vi} from 'vitest';
import {act} from 'react';
import {createRoot} from 'react-dom/client';
import {SelectedCompanySections} from '../SelectedCompanySections';
import {getUsersByCompany} from '../../../services/userService';
vi.mock('../../../services/userService',()=>({getUsersByCompany:vi.fn()}));
vi.mock('../AiUsagePanel',()=>({AiUsagePanel:({selectedCompany}:{selectedCompany:{id:string}})=><p>Usage {selectedCompany.id}</p>}));
it('drops late users from the previous company when selection changes',async()=>{
 let resolve!: (value:unknown)=>void;
 vi.mocked(getUsersByCompany).mockImplementationOnce(()=>new Promise(r=>{resolve=r as typeof resolve;}));
 vi.mocked(getUsersByCompany).mockResolvedValueOnce({kind:'success',data:[{id:'b',email:'b@example.com',role:'technician',status:'active'}]});
 const host=document.createElement('div'),root=createRoot(host),token=async()=>null;
 try{
  await act(async()=>root.render(<SelectedCompanySections key="a" companyId="a" token={token}/>));
  await act(async()=>root.render(<SelectedCompanySections key="b" companyId="b" token={token}/>));
  await act(async()=>resolve({kind:'success',data:[{id:'a',email:'a@example.com',role:'technician',status:'active'}]}));
  expect(host.textContent).toContain('b@example.com');expect([...host.querySelectorAll('th')].map(e=>e.textContent)).toEqual(['Nom','Email','Rôle','État']);expect(host.querySelectorAll('tbody tr')).toHaveLength(1);expect(host.textContent).not.toContain('a@example.com');
 }finally{await act(async()=>root.unmount());}
});
