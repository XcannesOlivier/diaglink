import {beforeEach,afterEach,it,expect,vi} from 'vitest';
import {act} from 'react';import{createRoot,type Root}from'react-dom/client';
import{StripeMachineStatusPanel}from'../StripeMachineStatusPanel';
import type{StripeCompanySummary}from'../../../services/stripeAdminService';
const{send}=vi.hoisted(()=>({send:vi.fn()}));
vi.mock('../../../services/stripeAdminService',()=>({setStripeMachineStatus:send}));
let box:HTMLDivElement;let root:Root;
beforeEach(()=>{send.mockReset();localStorage.clear();box=document.createElement('div');document.body.append(box);root=createRoot(box);});
afterEach(async()=>{await act(async()=>root.unmount());box.remove();localStorage.clear();});
it('shows billable state and paid rights, retries the same command after a lost response',async()=>{
 const refresh=vi.fn();const token=vi.fn();
 const account={testActionsEnabled:true,machines:[{id:'m',name:'Machine',billable:true,rightsEndUtc:'2026-10-14T18:28:51Z'}]}as StripeCompanySummary;
 await act(async()=>root.render(<StripeMachineStatusPanel companyId="c" account={account} token={token} refresh={refresh}/>));
 expect(box.textContent).toContain('Facturable');expect(box.textContent).toContain('14 octobre 2026');
 send.mockResolvedValue({kind:'error'});await act(async()=>box.querySelector('button')!.click());
 const id=send.mock.calls[0][4];expect(refresh).not.toHaveBeenCalled();
 send.mockResolvedValue({kind:'success',data:{status:'Synchronized'}});await act(async()=>box.querySelector('button')!.click());
 expect(send).toHaveBeenLastCalledWith(token,'c','m',false,id);expect(box.textContent).toContain('Modification enregistrée.');expect(refresh).toHaveBeenCalledOnce();
});
