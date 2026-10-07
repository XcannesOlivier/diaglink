import {act,type MouseEventHandler,type ReactNode} from 'react';
import {createRoot,type Root} from 'react-dom/client';
import {readFileSync} from 'node:fs';import {resolve} from 'node:path';
import {afterEach,beforeEach,describe,expect,it,vi} from 'vitest';
import {AgentChat} from '../AgentChat';
import {StarterMessages} from '../chat/StarterMessages';
import {clearDiagLinkSession,markMachineEntryChoiceHandled} from '../../utils/apiAuth';

const mocks=vi.hoisted(()=>({dispatch:vi.fn(),dispatchToast:vi.fn(),state:null as unknown as ReturnType<typeof makeState>}));
const makeState=(role:'technician'|'company_admin'|'diaglink_super_admin')=>({
 auth:{status:'authenticated',user:null,error:null,currentUser:{userId:'u1',companyId:'c1',role}},
 chat:{status:'idle',messages:[],currentConversationId:null,error:null,pendingMessages:[]},
 conversations:{list:[],isLoading:false,sidebarOpen:false,hasMore:false},
 ui:{chatInputEnabled:true,currentView:'chat'},
 machine:{selected:{id:'machine-1',name:'Compresseur Test Alpha',reference:null}},
});

vi.mock('@fluentui/react-components',()=>({
 Button:({children,onClick}:{children?:ReactNode;onClick?:MouseEventHandler<HTMLButtonElement>})=><button type="button" onClick={onClick}>{children}</button>,
 Toaster:()=>null,Toast:({children}:{children?:ReactNode})=><div>{children}</div>,ToastTitle:({children}:{children?:ReactNode})=><div>{children}</div>,
 Body1:({children}:{children?:ReactNode})=><p>{children}</p>,Subtitle1:({children}:{children?:ReactNode})=><strong>{children}</strong>,
 useId:()=> 'machine-toaster',useToastController:()=>({dispatchToast:mocks.dispatchToast}),
}));
vi.mock('../../hooks/useAppState',()=>({useAppState:()=>({chat:mocks.state.chat,state:mocks.state})}));
vi.mock('../../contexts/AppContext',()=>({useAppContext:()=>({dispatch:mocks.dispatch})}));
vi.mock('../../hooks/useAuth',()=>({useAuth:()=>({getAccessToken:async()=>null})}));
vi.mock('../../services/chatService',()=>({ChatService:class{clearError(){}cancelStream(){}clearChat(){}deleteConversation(){return Promise.resolve();}sendMessage(){return Promise.resolve();}listConversations(){return Promise.resolve({conversations:[],hasMore:false});}getConversationMessages(){return Promise.resolve([]);}}}));
vi.mock('../../services/machineService',()=>({getMachines:vi.fn()}));
vi.mock('../../services/telemetry',()=>({trackFeedback:vi.fn()}));
vi.mock('../ConversationSidebar',()=>({ConversationSidebar:()=>null}));
vi.mock('../ChatInterface',()=>({ChatInterface:({starterAccessory,agentName,machineId}:{starterAccessory?:ReactNode;agentName?:string;machineId?:string})=><section aria-label="Assistant Technique" data-machine-id={machineId}><span>{agentName}</span>{starterAccessory}</section>}));

describe('AgentChat machine confirmation panel',()=>{
 let host:HTMLDivElement,root:Root;
 beforeEach(()=>{sessionStorage.clear();mocks.dispatch.mockClear();mocks.state=makeState('company_admin');host=document.createElement('div');document.body.append(host);root=createRoot(host);});
 afterEach(async()=>{await act(async()=>root.unmount());host.remove();sessionStorage.clear();});
 const render=async()=>act(async()=>root.render(<AgentChat agentId="agent" agentName="Assistant Technique — Compresseur Test Alpha"/>));
 const remount=async()=>{await act(async()=>root.unmount());root=createRoot(host);await render();};

 it('keeps only the allowed actions, preserves the machine title elsewhere and confirms through sessionStorage',async()=>{
  await render();
  const panel=host.querySelector<HTMLElement>('[aria-label="Confirmation de la machine sélectionnée"]')!;
  expect(panel).not.toBeNull();expect(panel.textContent).not.toContain('Machine actuelle');expect(panel.textContent).not.toContain('Compresseur Test Alpha');
  expect(host.querySelector('[aria-label="Assistant Technique"]')?.textContent).toContain('Compresseur Test Alpha');
  expect([...panel.querySelectorAll('button')].map(button=>button.textContent)).toEqual(['Garder cette machine','Changer de machine','Administration']);
  await act(async()=>[...panel.querySelectorAll('button')][0].click());
  expect(sessionStorage.getItem('diaglink_machine_entry_choice_handled')).toBe('true');
  expect(host.querySelector('[aria-label="Confirmation de la machine sélectionnée"]')).toBeNull();
  mocks.state={...mocks.state,machine:{selected:{id:'machine-2',name:'Nouvelle machine',reference:null}}};await render();
  expect(host.querySelector('[aria-label="Confirmation de la machine sélectionnée"]')).toBeNull();
  expect(host.querySelector('[aria-label="Assistant Technique"]')?.getAttribute('data-machine-id')).toBe('machine-2');
  await remount();expect(host.querySelector('[aria-label="Confirmation de la machine sélectionnée"]')).toBeNull();
 });

 it('does not grant Administration to a technician and keeps the existing change-machine action',async()=>{
  mocks.state=makeState('technician');await render();
  const panel=host.querySelector<HTMLElement>('[aria-label="Confirmation de la machine sélectionnée"]')!;
  expect([...panel.querySelectorAll('button')].map(button=>button.textContent)).toEqual(['Garder cette machine','Changer de machine']);
  await act(async()=>[...panel.querySelectorAll('button')][1].click());
  expect(mocks.dispatch).toHaveBeenCalledWith({type:'UI_SET_VIEW',view:'machines'});
  expect(sessionStorage.getItem('diaglink_machine_entry_choice_handled')).toBe('true');
  mocks.state={...mocks.state,machine:{selected:{id:'machine-2',name:'Nouvelle machine',reference:null}}};await remount();
  expect(host.querySelector('[aria-label="Confirmation de la machine sélectionnée"]')).toBeNull();
  expect(host.querySelector('[aria-label="Assistant Technique"]')?.getAttribute('data-machine-id')).toBe('machine-2');
 });

 it('honors the handled state after refresh and resets it for a new authenticated session',async()=>{
  markMachineEntryChoiceHandled();await render();
  expect(host.querySelector('[aria-label="Confirmation de la machine sélectionnée"]')).toBeNull();
  await remount();expect(host.querySelector('[aria-label="Confirmation de la machine sélectionnée"]')).toBeNull();
  await act(async()=>root.unmount());clearDiagLinkSession();root=createRoot(host);await render();
  expect(host.querySelector('[aria-label="Confirmation de la machine sélectionnée"]')).not.toBeNull();
 });

 it('places the compact actions between the technical-assistant title and unchanged starter messages',async()=>{
  await act(async()=>root.render(<StarterMessages agentName="Assistant Technique Compresseur Test Alpha" accessory={<div data-confirmation-actions/>} onPromptClick={vi.fn()}/>));
  const actions=host.querySelector('[data-confirmation-actions]')!,prompts=host.querySelector('ul')!;
  expect(host.textContent).toContain('Assistant Technique');expect(host.textContent).toContain('Compresseur Test Alpha');
  expect(host.textContent).toContain('Diagnostiquer un dysfonctionnement');expect(host.textContent).toContain('Identifier un composant');expect(host.textContent).toContain('Interpréter un schéma technique');
  expect(actions.compareDocumentPosition(prompts)&Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  const css=readFileSync(resolve('src/components/AgentChat.module.css'),'utf8');
  expect(css).toMatch(/\.machineConfirmationCard\s*\{[^}]*width:\s*fit-content;[^}]*margin:\s*0 auto;/s);
  expect(css).toMatch(/\.machineConfirmationActions\s*\{[^}]*justify-content:\s*center;[^}]*flex-wrap:\s*wrap;/s);
  expect(css).toMatch(/@media \(max-width: 768px\)[\s\S]*\.machineConfirmationCard\s*\{[^}]*width:\s*100%;/);
 });
});
