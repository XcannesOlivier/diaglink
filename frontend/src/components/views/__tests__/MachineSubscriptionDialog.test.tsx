import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MachineSubscriptionDialog } from '../MachineSubscriptionDialog';
import { CompanyConsumptionPanel } from '../CompanyConsumptionPanel';

const { setStatus, setCompanyStatus } = vi.hoisted(() => ({ setStatus: vi.fn(), setCompanyStatus: vi.fn() }));
vi.mock('../../../services/stripeAdminService', () => ({ setStripeMachineStatus: setStatus, setCompanyStripeMachineStatus: setCompanyStatus }));
vi.mock('../../../utils/apiAuth', () => ({ getApiAuthHeaders: async () => ({ headers: {} }) }));

describe('gestion de l’abonnement machine', () => {
  let host: HTMLDivElement;
  let root: Root;
  const token = vi.fn();
  const activeMachine = { id: 'm1', name: 'Grue', billable: true, hasPaidRights: true };

  beforeEach(() => {
    setStatus.mockReset();
    setCompanyStatus.mockReset();
    localStorage.clear();
    host = document.createElement('div');
    document.body.append(host);
    root = createRoot(host);
  });
  afterEach(async () => {
    await act(async () => root.unmount());
    host.remove();
    localStorage.clear();
    vi.unstubAllGlobals();
  });

  const button = (label: string) => [...document.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent === label)!;

  it('place Gérer à côté du nom et ouvre uniquement les actions de la bonne machine', async () => {
    const metrics={responses:0,vision:0,summaries:0,input:0,output:0,tokens:0,unknown:0,unvalued:0,realCost:0,commercialCredit:0,walletRealCost:0,providers:[],includedQuotaConsumed:0};
    const machine=(id:string,name:string,billable:boolean)=>({id,name,billable,budget:10,used:0,remaining:10,resetUtc:billable?'2026-10-01T00:00:00Z':null,rightsEndUtc:null,hasPaidRights:true,metrics,users:[]});
    vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response(JSON.stringify({companyName:'Entreprise',walletBalance:0,metrics,machines:[machine('m1','Grue',true),machine('m2','Tour',false)]}))));
    await act(async()=>root.render(<CompanyConsumptionPanel companyId="c1" token={token} view="machines" machineActionsEnabled onMachineSubscriptionChanged={vi.fn()}/>));

    const cards=[...host.querySelectorAll<HTMLElement>('[data-machine-card]')];
    expect(cards).toHaveLength(2);
    expect(cards[0].querySelector('header div')?.textContent).toBe('GrueGérer');
    expect(cards[1].querySelector('header div')?.textContent).toBe('TourGérer');
    await act(async()=>[...cards[0].querySelectorAll<HTMLButtonElement>('button')].find(item=>item.textContent==='Gérer')!.click());
    let dialog=document.querySelector<HTMLElement>('[role="dialog"]')!;
    expect(dialog.textContent).toContain('Gérer la machine — Grue');
    expect(dialog.textContent).toContain('Cette machine ne sera plus renouvelée à la prochaine échéance.');
    expect(dialog.textContent).not.toContain('Quota inclus');expect(dialog.textContent).not.toContain('Crédit supplémentaire');expect(dialog.textContent).not.toContain('Abonnement :');
    expect([...dialog.querySelectorAll('button')].filter(item=>item.textContent==='Résilier l’abonnement')).toHaveLength(1);
    await act(async()=>button('Annuler').click());

    await act(async()=>[...cards[1].querySelectorAll<HTMLButtonElement>('button')].find(item=>item.textContent==='Gérer')!.click());
    dialog=document.querySelector<HTMLElement>('[role="dialog"]')!;
    expect(dialog.textContent).toContain('Gérer la machine — Tour');
    expect(dialog.textContent).toContain('Réactiver le renouvellement');
    expect(dialog.textContent).toContain('Aucun montant supplémentaire ne sera facturé aujourd’hui.');
    expect([...dialog.querySelectorAll('button')].some(item=>item.textContent==='Résilier l’abonnement')).toBe(false);
    expect([...dialog.querySelectorAll('button')].some(item=>item.textContent==='Réactiver le renouvellement')).toBe(true);
  });

  it('réactive sans prorata le renouvellement d’une machine encore couverte', async () => {
    let finish!:(value:unknown)=>void;
    setStatus.mockImplementation(()=>new Promise(resolve=>{finish=resolve;}));
    const machine={id:'m2',name:'Tour',billable:false,hasPaidRights:true},onOpenChange=vi.fn(),onSuccess=vi.fn();
    await act(async()=>root.render(<MachineSubscriptionDialog open machine={machine} companyId="c1" token={token} actionEnabled onOpenChange={onOpenChange} onSuccess={onSuccess}/>));
    expect(document.body.textContent).toContain('Cette machine sera de nouveau renouvelée à la prochaine échéance. Aucun montant supplémentaire ne sera facturé aujourd’hui.');
    await act(async()=>button('Réactiver le renouvellement').click());
    expect(document.body.textContent).toContain('Confirmer la réactivation du renouvellement ?');
    expect(document.body.textContent).toContain('La machine sera de nouveau incluse dans le prochain renouvellement.');
    const confirm=button('Confirmer la réactivation');
    await act(async()=>{confirm.click();confirm.click();});
    expect(setStatus).toHaveBeenCalledOnce();expect(setStatus).toHaveBeenCalledWith(token,'c1','m2',true,expect.any(String));
    await act(async()=>finish({kind:'success',data:{status:'Synchronized'}}));
    expect(onOpenChange).toHaveBeenCalledWith(false);expect(onSuccess).toHaveBeenCalledOnce();
    expect(localStorage.getItem('diaglink:machine-status:c1:m2')).toBeNull();
  });

  it('utilise la route Company Admin sans transmettre le companyId', async () => {
    setCompanyStatus.mockResolvedValue({kind:'success',data:{status:'Synchronized'}});
    const onSuccess=vi.fn();
    await act(async()=>root.render(<MachineSubscriptionDialog open machine={activeMachine} companyId="company-local" token={token} actionEnabled companyScoped onOpenChange={vi.fn()} onSuccess={onSuccess}/>));
    const actions=document.querySelector('[data-company-machine-actions]')!;
    expect(Array.from(actions.querySelectorAll(':scope > button')).map(item=>item.textContent)).toEqual(['Annuler','Résilier l’abonnement']);
    await act(async()=>button('Résilier l’abonnement').click());
    expect(Array.from(actions.querySelectorAll(':scope > button')).map(item=>item.textContent)).toEqual(['Annuler','Confirmer la résiliation']);
    await act(async()=>button('Confirmer la résiliation').click());
    expect(setCompanyStatus).toHaveBeenCalledWith(token,'m1',false,expect.any(String));
    expect(setStatus).not.toHaveBeenCalled();
    expect(onSuccess).toHaveBeenCalledOnce();
  });

  it('réutilise le flux serveur payant pour une machine expirée sans simuler de montant', async () => {
    setStatus.mockResolvedValue({kind:'success',data:{status:'AwaitingPayment'}});
    const machine={id:'m3',name:'Presse',billable:false,hasPaidRights:false},onOpenChange=vi.fn(),onSuccess=vi.fn();
    await act(async()=>root.render(<MachineSubscriptionDialog open machine={machine} companyId="c1" token={token} actionEnabled onOpenChange={onOpenChange} onSuccess={onSuccess}/>));
    expect(document.body.textContent).toContain('Réactiver l’abonnement');
    expect(document.body.textContent).toContain('Le montant jusqu’au prochain renouvellement sera calculé au prorata.');
    expect(document.body.textContent).not.toContain('Montant à payer aujourd’hui');
    await act(async()=>button('Réactiver l’abonnement').click());
    expect(document.body.textContent).toContain('Confirmer la réactivation ?');
    expect(document.body.textContent).toContain('le montant au prorata sera facturé');
    await act(async()=>button('Confirmer et réactiver').click());
    expect(setStatus).toHaveBeenCalledWith(token,'c1','m3',true,expect.any(String));
    expect(onOpenChange).toHaveBeenCalledWith(false);expect(onSuccess).toHaveBeenCalledOnce();
    expect(JSON.parse(localStorage.getItem('diaglink:machine-status:c1:m3')!)).toMatchObject({active:true});
  });

  it('termine la réactivation payante seulement après confirmation du serveur', async () => {
    setStatus.mockResolvedValue({kind:'success',data:{status:'Completed'}});
    const machine={id:'m3',name:'Presse',billable:false,hasPaidRights:false},onOpenChange=vi.fn(),onSuccess=vi.fn();
    await act(async()=>root.render(<MachineSubscriptionDialog open machine={machine} companyId="c1" token={token} actionEnabled onOpenChange={onOpenChange} onSuccess={onSuccess}/>));
    await act(async()=>button('Réactiver l’abonnement').click());await act(async()=>button('Confirmer et réactiver').click());
    expect(localStorage.getItem('diaglink:machine-status:c1:m3')).toBeNull();expect(onSuccess).toHaveBeenCalledOnce();
  });

  it('conserve la reprise et demande un refresh si le paiement n’est pas confirmé', async () => {
    setStatus.mockResolvedValue({kind:'conflict',message:'Paiement non confirmé.'});
    const machine={id:'m3',name:'Presse',billable:false,hasPaidRights:false},onOpenChange=vi.fn(),onSuccess=vi.fn();
    await act(async()=>root.render(<MachineSubscriptionDialog open machine={machine} companyId="c1" token={token} actionEnabled onOpenChange={onOpenChange} onSuccess={onSuccess}/>));
    await act(async()=>button('Réactiver l’abonnement').click());await act(async()=>button('Confirmer et réactiver').click());
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();expect(document.querySelector('[role="alert"]')?.textContent).toBe('Paiement non confirmé.');
    expect(onOpenChange).not.toHaveBeenCalled();expect(onSuccess).toHaveBeenCalledOnce();
    expect(JSON.parse(localStorage.getItem('diaglink:machine-status:c1:m3')!)).toMatchObject({active:true});
  });

  it('impose une confirmation et Annuler ne déclenche aucune requête', async () => {
    await act(async()=>root.render(<MachineSubscriptionDialog open machine={activeMachine} companyId="c1" token={token} actionEnabled onOpenChange={vi.fn()} onSuccess={vi.fn()}/>));
    await act(async()=>button('Résilier l’abonnement').click());
    expect(setStatus).not.toHaveBeenCalled();expect(document.body.textContent).toContain('Confirmer la résiliation ?');
    await act(async()=>button('Annuler').click());
    expect(setStatus).not.toHaveBeenCalled();expect(document.body.textContent).toContain('Cette machine ne sera plus renouvelée à la prochaine échéance.');
  });

  it('empêche les doubles clics puis demande le rafraîchissement après succès', async () => {
    let finish!:(value:unknown)=>void;
    setStatus.mockImplementation(()=>new Promise(resolve=>{finish=resolve;}));
    const onOpenChange=vi.fn(),onSuccess=vi.fn();
    await act(async()=>root.render(<MachineSubscriptionDialog open machine={activeMachine} companyId="c1" token={token} actionEnabled onOpenChange={onOpenChange} onSuccess={onSuccess}/>));
    await act(async()=>button('Résilier l’abonnement').click());
    const confirm=button('Confirmer la résiliation');
    await act(async()=>{confirm.click();confirm.click();});
    expect(setStatus).toHaveBeenCalledOnce();
    expect(setStatus).toHaveBeenCalledWith(token,'c1','m1',false,expect.any(String));
    expect([...document.querySelectorAll<HTMLButtonElement>('button')].find(item=>item.textContent?.includes('Résiliation…'))?.disabled).toBe(true);
    await act(async()=>finish({kind:'success',data:{status:'Synchronized'}}));
    expect(onOpenChange).toHaveBeenCalledWith(false);expect(onSuccess).toHaveBeenCalledOnce();
    expect(localStorage.getItem('diaglink:machine-status:c1:m1')).toBeNull();
  });

  it('conserve la modale ouverte et affiche l’erreur si la résiliation échoue', async () => {
    setStatus.mockResolvedValue({kind:'conflict',message:'Opération en cours.'});
    const onOpenChange=vi.fn(),onSuccess=vi.fn();
    await act(async()=>root.render(<MachineSubscriptionDialog open machine={activeMachine} companyId="c1" token={token} actionEnabled onOpenChange={onOpenChange} onSuccess={onSuccess}/>));
    await act(async()=>button('Résilier l’abonnement').click());
    await act(async()=>button('Confirmer la résiliation').click());
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();expect(document.querySelector('[role="alert"]')?.textContent).toBe('Opération en cours.');
    expect(onOpenChange).not.toHaveBeenCalled();expect(onSuccess).not.toHaveBeenCalled();
  });
});
