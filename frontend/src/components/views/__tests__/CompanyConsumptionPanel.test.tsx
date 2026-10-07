import {it,expect,vi,afterEach} from 'vitest';
import {act} from 'react';import {createRoot} from 'react-dom/client';
import {readFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {CompanyConsumptionPanel} from '../CompanyConsumptionPanel';
import styles from '../CompanyFinancePanel.module.css';
import {darkTheme,lightTheme} from '../../../config/themes';
vi.mock('../../../utils/apiAuth',()=>({getApiAuthHeaders:async()=>({headers:{}})}));
const metrics={responses:16,vision:2,summaries:1,input:100,output:20,tokens:120,unknown:1,unvalued:1,realCost:3.18,commercialCredit:2.4,walletRealCost:.8,providers:[{provider:'test',model:'model',count:3}],includedQuotaConsumed:2.4};
const machine=(id:string,name:string,n:number)=>({id,name,billable:id==='a',budget:10,used:10,remaining:0,resetUtc:'2026-11-14T00:00:00Z',rightsEndUtc:'2026-11-14T00:00:00Z',hasPaidRights:true,metrics:{...metrics,responses:n},users:[{id:'u',name:'Olivier',metrics:{...metrics,responses:n,commercialCredit:.3,includedQuotaConsumed:n===16?2.4:1.1}}]});
const zeroMachine={...machine('b','Machine B',999),budget:0,used:0,remaining:0,resetUtc:null,users:[{id:'u',name:'Olivier',metrics:{...metrics,responses:999,commercialCredit:0,includedQuotaConsumed:0}}]};
const report={companyName:'Entreprise',walletBalance:49.96,metrics,machines:[machine('a','Machine A',16),zeroMachine]};
const buttonByDesktopLabel=(root:ParentNode,label:string)=>[...root.querySelectorAll<HTMLButtonElement>('button')].find(button=>button.querySelector(`.${styles.desktopButtonLabel}`)?.textContent===label)!;
afterEach(()=>vi.unstubAllGlobals());

it('renders one compact summary card per machine with unchanged values and zero-quota support',async()=>{
 vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response(JSON.stringify(report))));
 const host=document.createElement('div'),root=createRoot(host);
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={async()=>null} view="machines"/>));
  const cards=[...host.querySelectorAll<HTMLElement>('[data-machine-card]')];
  expect(cards).toHaveLength(2);expect(host.querySelector('table')).toBeNull();expect(host.textContent).not.toContain('Tous les usages');
  expect(host.querySelector('select[aria-label="Période de consommation par machine"]')).not.toBeNull();
  const first=cards[0];
  expect(first.textContent).toContain('Machine A');expect(first.textContent).not.toContain('Active · facturable');expect(first.textContent).toContain('Quota mensuel : Épuisé');expect(first.textContent).not.toContain('Quota mensuel : Actif');expect(first.textContent).not.toContain('Quota mensuel : Non actif');expect(first.textContent).not.toContain('Fin des droits');expect(first.textContent).not.toContain('14 novembre 2026');
  expect([...first.querySelectorAll('h4')].map(node=>node.textContent)).toEqual(['Quota inclus','Crédit supplémentaire consommé','État']);
  expect(first.textContent).toContain('Abonnement : Actif');
  const firstQuotaCard=[...first.querySelectorAll('article')].find(card=>card.querySelector('h4')?.textContent==='Quota inclus')!;
  expect(firstQuotaCard.querySelector(':scope > strong')).toBeNull();expect(firstQuotaCard.textContent).toContain('Consommé : 10,00');expect(firstQuotaCard.textContent).toContain('Restant : 0,00');expect(first.textContent).toContain('2,40');
  expect(first.querySelector('progress')?.value).toBe(0);expect(first.querySelector('progress')?.max).toBe(100);
  expect(first.classList.contains(styles.machineConsumptionCardAlternate)).toBe(true);
  expect([...first.querySelectorAll('div[class*="consumptionMetrics"] article')].every(card=>card.classList.contains(styles.consumptionSummaryCard)&&!card.classList.contains(styles.machineConsumptionCardAlternate))).toBe(true);
  const second=cards[1];
  expect(second.textContent).toContain('Machine B');expect(second.textContent).not.toContain('Active · facturable');expect(second.textContent).toContain('Renouvellement : Arrêté');expect(second.textContent).toContain('Accès : Actif');expect(second.textContent).not.toContain('Abonnement : Inactif');expect(second.textContent).toContain('Quota mensuel : Non actif');expect(second.textContent).not.toContain('Fin des droits');expect(second.textContent).not.toContain('14 novembre 2026');
  const secondQuotaCard=[...second.querySelectorAll('article')].find(card=>card.querySelector('h4')?.textContent==='Quota inclus')!;
  expect(secondQuotaCard.querySelector(':scope > strong')).toBeNull();expect(secondQuotaCard.textContent).toContain('Consommé : 0,00');expect(secondQuotaCard.textContent).toContain('Restant : 0,00');
  expect(second.querySelector('progress')?.value).toBe(0);expect(host.textContent).not.toContain('49,96');
  expect(second.classList.contains(styles.machineConsumptionCardAlternate)).toBe(false);
  expect(first.querySelector(`.${styles.machineConsumptionMetrics}`)?.querySelectorAll(':scope > article')).toHaveLength(3);
  expect(second.querySelector(`.${styles.machineConsumptionMetrics}`)?.querySelectorAll(':scope > article')).toHaveLength(3);
  expect(first.querySelector('header h3')?.textContent).toBe('Machine A');expect(buttonByDesktopLabel(first,'Voir la consommation par utilisateur')).not.toBeNull();expect(buttonByDesktopLabel(first,'Historique des tokens')).not.toBeNull();expect(first.querySelector(':scope > button')).toBeNull();
  expect(second.querySelector('header')?.textContent).not.toContain('Quota mensuel');expect(second.querySelector('header button')).not.toBeNull();expect(second.querySelector(':scope > button')).toBeNull();
 }finally{await act(async()=>root.unmount());}
});

it('distingue abonnement actif, renouvellement arrêté avec accès et abonnement expiré sans afficher de date',async()=>{
 const machines=[machine('a','Active',1),{...machine('b','Couverte',2),billable:false,hasPaidRights:true},{...machine('c','Expirée',3),billable:false,hasPaidRights:false,resetUtc:null,rightsEndUtc:'2026-01-10T00:00:00Z'}];
 vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response(JSON.stringify({...report,machines}))));
 const host=document.createElement('div'),root=createRoot(host);
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={async()=>null} view="machines"/>));
  const cards=[...host.querySelectorAll<HTMLElement>('[data-machine-card]')];
  expect(cards[0].textContent).toContain('Abonnement : Actif');expect(cards[0].textContent).toContain('Quota mensuel : Épuisé');expect(cards[0].textContent).not.toContain('Renouvellement :');
  expect(cards[1].textContent).toContain('Renouvellement : Arrêté');expect(cards[1].textContent).toContain('Accès : Actif');expect(cards[1].textContent).toContain('Quota mensuel : Épuisé');expect(cards[1].textContent).not.toContain('Abonnement : Inactif');
  expect(cards[2].textContent).toContain('Abonnement : Inactif');expect(cards[2].textContent).not.toContain('Accès : Actif');expect(cards[2].textContent).toContain('Quota mensuel : Non actif');
  expect(host.textContent).not.toContain('Fin des droits');expect(host.textContent).not.toContain('10 janvier 2026');
 }finally{await act(async()=>root.unmount());}
});

it('distingue le quota épuisé, absent et disponible à partir du reste exact',async()=>{
 const machines=[
  {...machine('a','Disponible arrondi',1),budget:10,used:9.999,remaining:.001},
  {...machine('b','Épuisée',2),billable:false,hasPaidRights:true,remaining:0},
  {...machine('c','Sans période',3),billable:false,hasPaidRights:false,remaining:0,resetUtc:null},
 ];
 vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response(JSON.stringify({...report,machines}))));
 const host=document.createElement('div'),root=createRoot(host);
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={async()=>null} view="machines"/>));
  const cards=[...host.querySelectorAll<HTMLElement>('[data-machine-card]')];
  expect(cards[0].querySelector('progress')?.value).toBe(0);expect(cards[0].textContent).not.toContain('Quota mensuel :');
  expect(cards[1].textContent).toContain('Renouvellement : Arrêté');expect(cards[1].textContent).toContain('Accès : Actif');expect(cards[1].textContent).toContain('Quota mensuel : Épuisé');
  expect(cards[2].textContent).toContain('Quota mensuel : Non actif');
  expect(host.textContent).not.toContain('Quota mensuel : Actif');
 }finally{await act(async()=>root.unmount());}
});

it('affiche une intervention uniquement dans la carte de la machine exactement rattachée',async()=>{
 vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response(JSON.stringify(report))));
 const host=document.createElement('div'),root=createRoot(host),onExamine=vi.fn();
 const interventions=[
  {id:'op-a',type:'machine-addition' as const,machineId:'a',label:'Ajout à reprendre'},
  {id:'op-outside',type:'machine-addition' as const,machineId:'outside',label:'Ne doit pas apparaître'},
 ];
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={async()=>null} view="machines" machineInterventions={interventions} onExamineInterventions={onExamine}/>));
  const cards=[...host.querySelectorAll<HTMLElement>('[data-machine-card]')];
  expect(cards[0].textContent).toContain('Intervention requise');expect(cards[0].textContent).toContain('Ajout à reprendre');
  expect(cards[1].textContent).not.toContain('Intervention requise');expect(host.textContent).not.toContain('Ne doit pas apparaître');
  await act(async()=>[...cards[0].querySelectorAll<HTMLButtonElement>('button')].find(button=>button.textContent==='Examiner / reprendre')!.click());
  expect(onExamine).toHaveBeenCalledOnce();
 }finally{await act(async()=>root.unmount());}
});

it('alterne uniquement le fond complet des machines par index avec les tokens clair et sombre',async()=>{
 const machines=[machine('a','Machine A',16),zeroMachine,machine('c','Machine C',3),machine('d','Machine D',4)];
 vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response(JSON.stringify({...report,machines}))));
 const cssModules=styles as Record<string,string>,style=document.createElement('style');
 const cssText=readFileSync(resolve('src/components/views/CompanyFinancePanel.module.css'),'utf8');
 style.textContent=cssText.replace(/\.([A-Za-z][\w-]*)/g,(_,name:string)=>cssModules[name]?`.${cssModules[name]}`:`.${name}`).replaceAll('var(--colorNeutralBackground1Hover)','rgb(245, 245, 245)').replaceAll('var(--colorNeutralBackground1)','rgb(255, 255, 255)').replaceAll('var(--colorNeutralStroke2)','rgb(128, 128, 128)');document.head.append(style);
 const host=document.createElement('div');document.body.append(host);const root=createRoot(host);
 try{await act(async()=>root.render(<details className={styles.technical} open><CompanyConsumptionPanel companyId="c" token={async()=>null} view="machines"/></details>));
  const cards=[...host.querySelectorAll<HTMLElement>('[data-machine-card]')];
  expect(cards.map(card=>card.classList.contains(styles.machineConsumptionCardAlternate))).toEqual([true,false,true,false]);
  expect(cards.map(card=>getComputedStyle(card).borderTopColor)).toEqual(Array(4).fill('rgb(128, 128, 128)'));
  expect(cards.map(card=>getComputedStyle(card).backgroundColor)).toEqual(['rgb(245, 245, 245)','rgb(255, 255, 255)','rgb(245, 245, 245)','rgb(255, 255, 255)']);
  expect(cssText).not.toContain('machineConsumptionCardAccent');expect(cssText).toContain('.machineConsumptionCardAlternate{background:var(--colorNeutralBackground1Hover)}');
  const brightness=(color:string)=>{const value=color.replace('#','');return [0,2,4].reduce((sum,index)=>sum+Number.parseInt(value.slice(index,index+2),16),0);};
  expect(brightness(lightTheme.colorNeutralBackground1Hover)).toBeLessThan(brightness(lightTheme.colorNeutralBackground1));
  expect(brightness(darkTheme.colorNeutralBackground1Hover)).toBeGreaterThan(brightness(darkTheme.colorNeutralBackground1));
  expect(cards.flatMap(card=>[...card.querySelectorAll('div[class*="consumptionMetrics"] article')]).every(card=>card.classList.contains(styles.consumptionSummaryCard))).toBe(true);
  expect([...host.querySelectorAll('button')].some(button=>button.textContent==='Voir plus de machines')).toBe(false);
 }finally{await act(async()=>root.unmount());host.remove();style.remove();}
});

it('limite à cinq machines puis recherche en temps réel dans la liste déjà chargée',async()=>{
 const machines=[
  machine('a','Machine Alpha',16),machine('b','Grue mobile',2),machine('c','h4immo',3),machine('d','Tracteur',4),
  machine('e','Pompe',5),machine('f','Tour',6),machine('g','Presse',7),
 ];
 const fetch=vi.fn().mockImplementation(async()=>new Response(JSON.stringify({...report,machines})));vi.stubGlobal('fetch',fetch);
 const host=document.createElement('div'),root=createRoot(host);
 const cards=()=>[...host.querySelectorAll<HTMLElement>('[data-machine-card]')];
 const button=(label:string)=>[...host.querySelectorAll<HTMLButtonElement>('button')].find(item=>item.textContent===label)!;
 const search=()=>host.querySelector<HTMLInputElement>('input[aria-label="Rechercher une machine"]')!;
 const type=async(value:string)=>act(async()=>{const input=search();Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value')!.set!.call(input,value);input.dispatchEvent(new Event('input',{bubbles:true}));});
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={async()=>null} view="machines"/>));
  expect(cards().map(card=>card.dataset.machineCard)).toEqual(['Machine Alpha','Grue mobile','h4immo','Tracteur','Pompe']);
  expect(button('Voir plus de machines')).not.toBeNull();
  await act(async()=>button('Voir plus de machines').click());expect(cards()).toHaveLength(7);expect(button('Voir moins de machines')).not.toBeNull();
  await act(async()=>button('Voir moins de machines').click());expect(cards()).toHaveLength(5);

  await act(async()=>buttonByDesktopLabel(cards()[0],'Voir la consommation par utilisateur').click());
  await type('  MACHINE ALPHA  ');expect(cards().map(card=>card.dataset.machineCard)).toEqual(['Machine Alpha']);expect(cards()[0].textContent).toContain('Olivier');
  await type('H4');expect(cards().map(card=>card.dataset.machineCard)).toEqual(['h4immo']);expect(cards()[0].classList.contains(styles.machineConsumptionCardAlternate)).toBe(true);
  await type('tract');expect(cards().map(card=>card.dataset.machineCard)).toEqual(['Tracteur']);
  expect(host.textContent).not.toContain('Voir plus de machines');
  await type('introuvable');expect(cards()).toHaveLength(0);expect(host.textContent).toContain('Aucune machine trouvée.');
  expect(host.querySelector('select[aria-label="Période de consommation par machine"]')).not.toBeNull();expect(search()).not.toBeNull();
  await type('');const period=host.querySelector<HTMLSelectElement>('select[aria-label="Période de consommation par machine"]')!;
  await act(async()=>{period.value='week';period.dispatchEvent(new Event('change',{bubbles:true}));});
  expect(period.value).toBe('week');expect(fetch).toHaveBeenCalledTimes(2);expect(cards()).toHaveLength(5);
 }finally{await act(async()=>root.unmount());}
});

it('opens and closes each machine user consumption independently without navigation',async()=>{
 vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response(JSON.stringify(report))));
 const host=document.createElement('div'),root=createRoot(host),initialUrl=window.location.href;
 const buttons=()=>[...host.querySelectorAll<HTMLButtonElement>('button')].filter(button=>button.querySelector(`.${styles.desktopButtonLabel}`)?.textContent?.includes('consommation par utilisateur'));
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={async()=>null} view="machines"/>));
  expect(buttons().map(button=>button.querySelector(`.${styles.desktopButtonLabel}`)?.textContent)).toEqual(['Voir la consommation par utilisateur','Voir la consommation par utilisateur']);
  await act(async()=>buttons()[0].click());
  let cards=[...host.querySelectorAll<HTMLElement>('[data-machine-card]')];
  expect(cards[0].textContent).toContain('Masquer la consommation par utilisateur');expect(cards[0].textContent).toContain('Olivier');expect(cards[0].textContent).toContain('24 %');expect(cards[1].textContent).not.toContain('Olivier');
  expect([...cards[0].querySelectorAll('thead th')].map(header=>header.textContent)).toEqual(['Utilisateur','Quota inclus consommé','Part du quota inclus','Crédit supplémentaire consommé']);
  expect([...cards[0].querySelectorAll('tbody td')].map(cell=>cell.textContent)).toEqual(['Olivier','2,40 €','24 %','0,30 €']);
  expect(cards[0].textContent).not.toContain('Réponses IA');expect(cards[0].textContent).not.toContain('Coût IA réel');expect(cards[0].textContent).not.toContain('Vision');
  expect(cards[0].querySelector('table caption')?.textContent).toBe('Consommation par utilisateur — Machine A');expect(window.location.href).toBe(initialUrl);
  expect(cards[0].classList.contains(styles.machineConsumptionCardAlternate)).toBe(true);expect(cards[0].querySelector('#machine-users-0')).not.toBeNull();expect(cards[0].contains(cards[0].querySelector('#machine-users-0'))).toBe(true);
  await act(async()=>buttons()[1].click());cards=[...host.querySelectorAll<HTMLElement>('[data-machine-card]')];
  expect(cards[0].textContent).toContain('Olivier');expect(cards[1].textContent).toContain('Olivier');expect(cards[1].textContent).toContain('0 %');expect(cards[1].textContent).not.toContain('NaN');expect(cards[1].textContent).not.toContain('Infinity');
  await act(async()=>buttons()[0].click());cards=[...host.querySelectorAll<HTMLElement>('[data-machine-card]')];
  expect(cards[0].textContent).not.toContain('Olivier');expect(cards[1].textContent).toContain('Olivier');expect(window.location.href).toBe(initialUrl);
 }finally{await act(async()=>root.unmount());}
});

it('opens and closes Claude call details locally while preserving the main token values',async()=>{
 const history={items:[
  {createdAtUtc:'2026-10-05T14:07:57Z',inputTokens:56358,outputTokens:4446,totalTokens:60804,model:'claude-sonnet-5',provider:'Anthropic',calls:[
   {callNumber:1,inputTokens:8120,outputTokens:280,totalTokens:8400,model:'claude-sonnet-5',stopReason:'tool_use',tools:['file_search']},
   {callNumber:2,inputTokens:48238,outputTokens:4166,totalTokens:52404,model:'claude-sonnet-5',stopReason:'end_turn',tools:[]},
  ]},
  {createdAtUtc:'2026-10-05T13:42:21Z',inputTokens:49046,outputTokens:1082,totalTokens:50128,model:'claude-sonnet-5',provider:'Anthropic',calls:[
   {callNumber:1,inputTokens:49046,outputTokens:1082,totalTokens:50128,model:'claude-sonnet-5',stopReason:'end_turn',tools:[]},
  ]},
 ],hasMore:false};
 const fetch=vi.fn().mockImplementation(async(input:string)=>new Response(JSON.stringify(String(input).includes('/token-history?')?history:report)));vi.stubGlobal('fetch',fetch);
 const host=document.createElement('div'),root=createRoot(host),numbers=new Intl.NumberFormat('fr-FR');
 try{
  await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={async()=>null} view="machines"/>));
  const card=host.querySelector<HTMLElement>('[data-machine-card="Machine A"]')!;
  const userButton=buttonByDesktopLabel(card,'Voir la consommation par utilisateur');
  const historyButton=[...card.querySelectorAll<HTMLButtonElement>('button')].find(button=>button.querySelector(`.${styles.desktopButtonLabel}`)?.textContent==='Historique des tokens')!;
  await act(async()=>userButton.click());expect(card.querySelector('#machine-users-0')).not.toBeNull();
  await act(async()=>{historyButton.click();await new Promise(resolve=>window.setTimeout(resolve,0));});
  expect(card.querySelector('#machine-users-0')).toBeNull();expect(card.querySelector('#machine-token-history-0')).not.toBeNull();
  await act(async()=>userButton.click());expect(card.querySelector('#machine-users-0')).not.toBeNull();expect(card.querySelector('#machine-token-history-0')).toBeNull();
  await act(async()=>historyButton.click());expect(card.querySelector('#machine-users-0')).toBeNull();expect(card.querySelector('#machine-token-history-0')).not.toBeNull();

  const mainTable=card.querySelector<HTMLTableElement>('#machine-token-history-0 > div > table')!;
  expect(mainTable.classList.contains(styles.tokenHistoryTable)).toBe(true);expect(mainTable.parentElement?.classList.contains(styles.tokenHistoryScroll)).toBe(true);
  expect([...mainTable.tHead!.rows[0].cells]).toHaveLength(10);
  expect([...mainTable.tHead!.rows[0].cells].slice(1,8).map(cell=>cell.textContent)).toEqual(['Input','Cache lu','Cache créé','Cache 5 min','Cache 1 h','Output','Total']);
  let rows=[...mainTable.tBodies[0].rows];
  expect([...rows[0].cells].slice(1,9).map(cell=>cell.textContent)).toEqual([numbers.format(56358),'0','0','0','0',numbers.format(4446),numbers.format(60804),'claude-sonnet-5']);
  expect(rows[0].querySelector('button')?.textContent).toBe('Détails');
  expect(rows[1].querySelector('button')?.textContent).toBe('Détails');
  const fetchCount=fetch.mock.calls.length;

  await act(async()=>rows[0].querySelector<HTMLButtonElement>('button')!.click());
  rows=[...mainTable.tBodies[0].rows];
  expect(rows[0].querySelector('button')?.textContent).toBe('Masquer');expect(rows).toHaveLength(3);
  const detailTable=rows[1].querySelector<HTMLTableElement>('table')!;
  expect(detailTable.classList.contains(styles.tokenHistoryTable)).toBe(true);expect(detailTable.parentElement?.classList.contains(styles.tokenHistoryScroll)).toBe(true);
  expect([...detailTable.tHead!.rows[0].cells]).toHaveLength(10);
  expect([...detailTable.tHead!.rows[0].cells].slice(0,8).map(cell=>cell.textContent)).toEqual(['Appel','Input','Cache lu','Cache créé','Cache 5 min','Cache 1 h','Output','Total']);
  const callRows=[...detailTable.tBodies[0].rows];
  expect([...callRows[0].cells].slice(0,8).map(cell=>cell.textContent)).toEqual(['1',numbers.format(8120),'0','0','0','0',numbers.format(280),numbers.format(8400)]);
  expect([...callRows[1].cells].slice(0,8).map(cell=>cell.textContent)).toEqual(['2',numbers.format(48238),'0','0','0','0',numbers.format(4166),numbers.format(52404)]);
  expect(fetch).toHaveBeenCalledTimes(fetchCount);

  await act(async()=>rows[2].querySelector<HTMLButtonElement>('button')!.click());
  rows=[...mainTable.tBodies[0].rows];
  expect(rows).toHaveLength(3);expect(rows[0].querySelector('button')?.textContent).toBe('Détails');expect(rows[1].querySelector('button')?.textContent).toBe('Masquer');
  expect(rows[2].textContent).toContain(numbers.format(49046));expect(rows[2].textContent).not.toContain(numbers.format(56358));
  await act(async()=>rows[1].querySelector<HTMLButtonElement>('button')!.click());
  expect([...mainTable.tBodies[0].rows]).toHaveLength(2);expect(mainTable.textContent).not.toContain('Détail des appels Claude');
  expect(fetch).toHaveBeenCalledTimes(fetchCount);
 }finally{await act(async()=>root.unmount());}
});

it('loads token history five rows at a time',async()=>{
 const items=Array.from({length:6},(_,index)=>({createdAtUtc:`2026-10-05T${String(14-index).padStart(2,'0')}:00:00Z`,inputTokens:100+index,outputTokens:10,totalTokens:110+index,model:'claude-sonnet-5',provider:'Anthropic',calls:null}));
 const fetch=vi.fn().mockImplementation(async(input:string)=>{
  const url=String(input);if(!url.includes('/token-history?'))return new Response(JSON.stringify(report));
  const skip=Number(new URL(url,'http://localhost').searchParams.get('skip')??0);
  return new Response(JSON.stringify({items:items.slice(skip,skip+5),hasMore:skip+5<items.length}));
 });vi.stubGlobal('fetch',fetch);
 const host=document.createElement('div'),root=createRoot(host);
 try{
  await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={async()=>null} view="machines"/>));
  const card=host.querySelector<HTMLElement>('[data-machine-card="Machine A"]')!;
  await act(async()=>{buttonByDesktopLabel(card,'Historique des tokens').click();await new Promise(resolve=>window.setTimeout(resolve,0));});
  const tokenCalls=()=>fetch.mock.calls.map(call=>String(call[0])).filter(url=>url.includes('/token-history?'));
  const rows=()=>card.querySelectorAll('#machine-token-history-0 > div > table > tbody > tr');
  expect(tokenCalls()).toEqual([expect.stringContaining('skip=0&take=5')]);expect(rows()).toHaveLength(5);
  await act(async()=>{[...card.querySelectorAll<HTMLButtonElement>('button')].find(button=>button.textContent==='Afficher plus')!.click();await new Promise(resolve=>window.setTimeout(resolve,0));});
  expect(tokenCalls()).toEqual([expect.stringContaining('skip=0&take=5'),expect.stringContaining('skip=5&take=5')]);expect(rows()).toHaveLength(6);expect(card.textContent).not.toContain('Afficher plus');
 }finally{await act(async()=>root.unmount());}
});

it('refreshes through the shared revision while preserving the selected period',async()=>{
 const allTime={...report,machines:report.machines.map(item=>({...item,budget:20,used:12,remaining:8}))};
 const fetch=vi.fn().mockImplementation(async(input:string)=>new Response(JSON.stringify(input.includes('from=')?report:allTime)));vi.stubGlobal('fetch',fetch);
 const host=document.createElement('div'),root=createRoot(host),token=async()=>null;
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={token} view="global" revision={0}/>));
  expect(String(fetch.mock.calls[0][0])).toContain('from=');expect(fetch.mock.calls.every(call=>!String(call[0]).includes('usageType='))).toBe(true);
  const period=host.querySelector<HTMLSelectElement>('select[aria-label="Période de consommation globale"]')!;
  expect([...period.options].map(option=>option.value)).toEqual(['today','week','month','previousMonth','thirtyDays','all']);
  await act(async()=>{period.value='all';period.dispatchEvent(new Event('change',{bubbles:true}));});
  expect(String(fetch.mock.lastCall?.[0])).not.toContain('from=');expect(String(fetch.mock.lastCall?.[0])).not.toContain('usageType=');
  const cards=[...host.querySelectorAll('div[class*="consumptionMetrics"] article')];
  expect(cards).toHaveLength(2);expect(cards.map(card=>card.querySelector('h4')?.textContent)).toEqual(['Quota inclus','Crédit supplémentaire consommé']);
  expect(cards.every(card=>card.classList.contains(styles.consumptionSummaryCard)&&!card.classList.contains(styles.machineConsumptionCardAlternate))).toBe(true);
  const quotaCard=cards[0];expect(quotaCard.querySelector(':scope > strong')?.textContent).toBe('40,00 €');expect(quotaCard.textContent).toContain('Consommé : 24,00');expect(quotaCard.textContent).toContain('Restant : 16,00');
  expect(quotaCard.textContent).not.toContain('% disponible');expect(quotaCard.querySelector('progress')?.value).toBe(40);expect(cards[1].querySelector('strong')?.textContent).toBe('4,80 €');
  expect(host.textContent).not.toContain('Tous les usages');expect(host.querySelector('table')).toBeNull();expect([...host.querySelectorAll('button')].some(button=>button.textContent==='Actualiser')).toBe(false);
  const calls=fetch.mock.calls.length;await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={token} view="global" revision={1}/>));
  expect(fetch).toHaveBeenCalledTimes(calls+1);expect(String(fetch.mock.lastCall?.[0])).not.toContain('from=');expect(period.value).toBe('all');
 }finally{await act(async()=>root.unmount());}
});

it('keeps global and machine periods independent',async()=>{
 const fetch=vi.fn().mockImplementation(async()=>new Response(JSON.stringify(report)));vi.stubGlobal('fetch',fetch);
 const host=document.createElement('div'),root=createRoot(host),token=async()=>null;
 const render=(revision:number)=>root.render(<><CompanyConsumptionPanel companyId="c" token={token} view="global" revision={revision}/><CompanyConsumptionPanel companyId="c" token={token} view="machines" revision={revision}/></>);
 try{await act(async()=>render(0));
  const globalPeriod=host.querySelector<HTMLSelectElement>('select[aria-label="Période de consommation globale"]')!,machinePeriod=host.querySelector<HTMLSelectElement>('select[aria-label="Période de consommation par machine"]')!;
  await act(async()=>{globalPeriod.value='all';globalPeriod.dispatchEvent(new Event('change',{bubbles:true}));});
  const calls=fetch.mock.calls.length;await act(async()=>render(1));
  expect(fetch).toHaveBeenCalledTimes(calls+2);expect(globalPeriod.value).toBe('all');expect(machinePeriod.value).toBe('month');
  const refreshedUrls=fetch.mock.calls.slice(-2).map(call=>String(call[0]));expect(refreshedUrls.some(url=>!url.includes('from='))).toBe(true);expect(refreshedUrls.some(url=>url.includes('from='))).toBe(true);
  expect(host.querySelector('section[aria-label="Consommation globale"] table')).toBeNull();expect(host.querySelectorAll('section[aria-label="Consommation par machine"] [data-machine-card]')).toHaveLength(2);
 }finally{await act(async()=>root.unmount());}
});

it('discards late responses after switching company',async()=>{
 let finish!:(value:Response)=>void;
 const fetch=vi.fn().mockImplementationOnce(()=>new Promise(resolve=>{finish=resolve;})).mockResolvedValue(new Response(JSON.stringify({...report,companyName:'New company',machines:[]})));vi.stubGlobal('fetch',fetch);
 const host=document.createElement('div'),root=createRoot(host),token=async()=>null;
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="old" token={token} view="machines"/>));await act(async()=>root.render(<CompanyConsumptionPanel companyId="new" token={token} view="machines"/>));await act(async()=>finish(new Response(JSON.stringify(report))));expect(host.textContent).not.toContain('Machine A');expect(fetch.mock.calls.some(call=>String(call[0]).includes('/companies/new/'))).toBe(true);
 }finally{await act(async()=>root.unmount());}
});

it('keeps assigned users with zero consumption and historical rows in the expanded detail',async()=>{
 const zero={responses:0,vision:0,summaries:0,input:0,output:0,tokens:0,unknown:0,unvalued:0,realCost:0,commercialCredit:0,walletRealCost:0,providers:[],includedQuotaConsumed:0};
 const users=[{id:'used',name:'Robert Petit',metrics},{id:'zero',name:'Technicien sans usage',metrics:zero},{id:'deleted',name:'Utilisateur non attribué / supprimé',metrics:{...zero,vision:1}}];
 vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response(JSON.stringify({...report,machines:[{...machine('a','Machine A',16),users}]}))));
 const host=document.createElement('div'),root=createRoot(host);
 try{await act(async()=>root.render(<CompanyConsumptionPanel companyId="c" token={async()=>null} view="machines"/>));await act(async()=>buttonByDesktopLabel(host,'Voir la consommation par utilisateur').click());
  const rows=[...host.querySelectorAll('table tbody tr')].map(row=>row.textContent);expect(rows).toHaveLength(3);expect(rows[0]).toContain('Robert Petit');expect(rows[0]).toContain('2,40 €');expect(rows[0]).toContain('24 %');expect(rows[1]).toContain('Technicien sans usage');expect(rows[1]).toContain('0,00 €');expect(rows[2]).toContain('Utilisateur non attribué / supprimé');
 }finally{await act(async()=>root.unmount());}
});
