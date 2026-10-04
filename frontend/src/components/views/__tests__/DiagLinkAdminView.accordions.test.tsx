import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AdminAccordionControl, AdminSection } from '../adminAccordion';
import { DiagLinkAdminView } from '../DiagLinkAdminView';

vi.mock('../../../hooks/useAuth', () => ({ useAuth: () => ({ getAccessToken: vi.fn() }) }));
vi.mock('../GlobalFinancePeriod', () => ({ GlobalFinancePeriod: () => null }));
vi.mock('../StripeAdminPanel', () => ({
  StripeAdminPanel: ({ accordion,selectedCompanyId,onSelectedCompanyChange,refreshRevision,onRefreshComplete }: { accordion: AdminAccordionControl;selectedCompanyId:string;onSelectedCompanyChange:(value:string)=>void;refreshRevision:number;onRefreshComplete:(revision:number)=>void }) => (
    <section aria-label="Sections financières de test">
      <select aria-label="Entreprise" value={selectedCompanyId} onChange={event=>onSelectedCompanyChange(event.target.value)}><option value="">Sélectionner</option><option value="company">Entreprise</option></select>
      {refreshRevision>0&&<button type="button" onClick={()=>onRefreshComplete(refreshRevision)}>Terminer le rafraîchissement</button>}
      {([
        ['globalConsumption', 'Consommation globale'],
        ['machineConsumption', 'Consommation par machine'],
        ['technical', 'Détails techniques'],
        ['repairs', 'Outils de réparation'],
      ] as const).map(([section, label]) => (
        <details
          key={section}
          data-primary-section={section}
          open={accordion.openSection === section}
          onToggle={event => {
            if (event.target !== event.currentTarget) return;
            accordion.onSectionToggle(section, event.currentTarget.open);
          }}
        >
          <summary>{label}</summary>
        </details>
      ))}
    </section>
  ),
}));

describe('DiagLinkAdminView primary accordions', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
  });

  const primaryDetails = () => [...container.querySelectorAll<HTMLDetailsElement>('[data-primary-section]')];
  const openPrimarySections = () => primaryDetails()
    .filter(details => details.open)
    .map(details => details.dataset.primarySection as AdminSection);
  const summary = (section: AdminSection) => container
    .querySelector<HTMLDetailsElement>(`[data-primary-section="${section}"]`)!
    .querySelector<HTMLElement>(':scope > summary')!;
  const click = async (element: HTMLElement) => {
    await act(async () => {
      element.click();
      await new Promise(resolve => setTimeout(resolve, 0));
    });
  };

  it('allows zero or one primary section to be open', async () => {
    await act(async () => root.render(<DiagLinkAdminView />));
    const statistics = container.querySelector<HTMLButtonElement>('button[aria-controls]')!;

    expect(statistics.getAttribute('aria-expanded')).toBe('false');
    expect(openPrimarySections()).toEqual([]);

    await click(summary('globalConsumption'));
    expect(openPrimarySections()).toEqual(['globalConsumption']);

    await click(summary('machineConsumption'));
    expect(openPrimarySections()).toEqual(['machineConsumption']);

    await click(statistics);
    expect(statistics.getAttribute('aria-expanded')).toBe('true');
    expect(openPrimarySections()).toEqual([]);

    await click(summary('technical'));
    expect(statistics.getAttribute('aria-expanded')).toBe('false');
    expect(openPrimarySections()).toEqual(['technical']);

    await click(summary('repairs'));
    expect(openPrimarySections()).toEqual(['repairs']);

    await click(summary('repairs'));
    expect(openPrimarySections()).toEqual([]);

  });

  it('provides one global refresh button in the page header with a loading state',async()=>{
    await act(async()=>root.render(<DiagLinkAdminView/>));
    const header=container.querySelector('[data-page-header]')!;
    const refresh=()=>[...container.querySelectorAll<HTMLButtonElement>('button')].find(button=>button.textContent==='Actualiser'||button.textContent==='Actualisation…')!;
    expect(refresh().disabled).toBe(true);expect(header.contains(refresh())).toBe(true);
    const company=container.querySelector<HTMLSelectElement>('select[aria-label="Entreprise"]')!;
    await act(async()=>{company.value='company';company.dispatchEvent(new Event('change',{bubbles:true}));});
    expect(refresh().disabled).toBe(false);await act(async()=>refresh().click());
    expect(refresh().textContent).toBe('Actualisation…');expect(refresh().disabled).toBe(true);
    expect([...container.querySelectorAll('button')].filter(button=>button.textContent==='Actualiser')).toHaveLength(0);
    await act(async()=>[...container.querySelectorAll<HTMLButtonElement>('button')].find(button=>button.textContent==='Terminer le rafraîchissement')!.click());
    expect(refresh().textContent).toBe('Actualiser');expect(refresh().disabled).toBe(false);
  });
});
