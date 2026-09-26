import {createPortal} from 'react-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { StripeCompanyPanel } from '../StripeCompanyPanel';
import type { AdminSection } from '../adminAccordion';
import styles from '../CompanyFinancePanel.module.css';
const { request } = vi.hoisted(() => ({ request: vi.fn() }));
vi.mock('../../../services/stripeAdminService', () => ({ stripeCompanyRequest: request }));
vi.mock('../StripeMachineAdditionsPanel', () => ({ StripeMachineAdditionsPanel: () => null }));
vi.mock('../WalletTopUpPanel', () => ({ WalletTopUpPanel: ({technicalTarget,repairTarget}: {technicalTarget?:HTMLElement|null;repairTarget?:HTMLElement|null}) => <>{technicalTarget&&createPortal(<section aria-label="Détails wallet"/>,technicalTarget)}{repairTarget&&createPortal(<section aria-label="Réparation wallet"/>,repairTarget)}<span>Crédit supplémentaire</span></> }));

describe('StripeCompanyPanel', () => {
  let root: Root;
  let container: HTMLDivElement;
  const token = vi.fn();
  const data = { stripeCustomerId: null, stripeSubscriptionId: null, subscriptionStatus: null,
    currentPeriodStartUtc: null, currentPeriodEndUtc: null, activeMachineCount: 2, testActionsEnabled: true,
    machines: [{ id: 'm1', name: 'Machine A', billable: true, rightsEndUtc: null }, { id: 'm2', name: 'Machine B', billable: true, rightsEndUtc: null }] };
  beforeEach(() => { request.mockReset(); container = document.createElement('div'); document.body.append(container); root = createRoot(container); });
  afterEach(async () => { await act(async () => root.unmount()); container.remove(); });
  async function render() { await act(async () => root.render(<StripeCompanyPanel companyId="c1" getAccessToken={token} />)); }
  async function renderControlled() {
    function ControlledPanel() {
      const [openSection, setOpenSection] = useState<AdminSection | null>(null);
      return <StripeCompanyPanel companyId="c1" getAccessToken={token} accordion={{
        openSection,
        onSectionToggle: (section, isOpen) => setOpenSection(current => isOpen ? section : current === section ? null : current),
      }} />;
    }
    await act(async () => root.render(<ControlledPanel />));
  }

  it('loads without mutations and refreshes only the recorded data', async () => {
    request.mockResolvedValueOnce({ kind: 'success', data }); await render();
    expect(request).toHaveBeenCalledExactlyOnceWith(token, 'c1');
    expect(container.textContent).toContain('Machines actives');
    expect(container.textContent).not.toContain('Créer / récupérer');
    request.mockResolvedValueOnce({ kind: 'success', data: { ...data, stripeCustomerId: 'cus_test' } });
    await act(async () => [...container.querySelectorAll('button')].find(b => b.textContent === 'Actualiser les données enregistrées')!.click());
    expect(request).toHaveBeenLastCalledWith(token, 'c1');
    expect(container.textContent).toContain('cus_test');
  });

  it('orders company sections without repeated summary cards', async () => {
    request.mockResolvedValue({ kind: 'success', data }); await render();
    expect([...container.querySelectorAll('summary')].map(s => s.textContent)).toEqual([
      'Consommation Agent', 'Crédits et paiements', 'État de facturationAucun abonnement enregistré · 2/2 machines · Aucun impayé · Aucune intervention',
      'Machines facturables (2)', 'Détails techniques', 'Outils de réparation'
    ]);
    expect(container.querySelectorAll('[aria-label="Résumé de l’entreprise"]')).toHaveLength(1);
    expect(container.textContent).not.toContain('Statut et consommation des machines');
    expect(container.textContent).not.toContain('Consommation IA globale de l’entreprise');
    expect(container.textContent).not.toContain('Crédit supplémentaire entreprise');
  });

  it('shows a business billing summary without claiming live Stripe synchronization', async () => {
    request.mockResolvedValue({ kind: 'success', data: { ...data, subscriptionStatus: 'active', amountRemainingCents: 1250 } }); await render();
    expect(container.textContent).toContain('État de facturation');
    expect(container.textContent).toContain('Actif · 2/2 machines · 12,50 € impayés · Aucune intervention');
    expect(container.textContent).not.toContain('Synchronisé avec Stripe');
  });

  it('keeps technical details and repair tools collapsed by default', async () => {
    request.mockResolvedValue({ kind: 'success', data: { ...data, stripeCustomerId: 'cus_test' } }); await render();
    const sections = [...container.querySelectorAll('details')].filter(details => {
      const text = details.querySelector('summary')?.textContent ?? '';
      return text.startsWith('État de facturation') || ['Détails techniques', 'Outils de réparation'].includes(text);
    });
    expect(sections).toHaveLength(3);
    expect(sections.every(details => !details.open)).toBe(true);
    const repairs = sections.find(details => details.querySelector('summary')?.textContent === 'Outils de réparation')!;
    expect(repairs.classList.contains(styles.repairTools)).toBe(false);
    expect(container.textContent).toContain('cus_test');
  });

  it('opens billing details and keeps machine management folded until requested', async () => {
    request.mockResolvedValue({ kind: 'success', data: { ...data, subscriptionStatus: 'active' } }); await render();
    const billing = [...container.querySelectorAll('details')].find(details => details.querySelector('summary')?.textContent?.startsWith('État de facturation'))!;
    expect(billing.open).toBe(false);
    expect(billing.querySelector('summary')?.textContent).toContain('Actif · 2/2 machines · Aucun impayé · Aucune intervention');
    await act(async () => billing.querySelector('summary')!.click());
    expect(billing.open).toBe(true);
    expect(billing.textContent).toContain('Opérations nécessitant une intervention');
    const machines = [...billing.querySelectorAll('details')].find(details => details.querySelector('summary')?.textContent === 'Machines facturables (2)')!;
    expect(machines.open).toBe(false);
    await act(async () => machines.querySelector('summary')!.click());
    expect(machines.open).toBe(true);
    const action = [...machines.querySelectorAll('button')].find(button => button.textContent === 'Désactiver Machine A');
    expect(action?.disabled).toBe(false);
  });

  it('keeps the five finance accordions exclusive without reacting to the nested machine section', async () => {
    request.mockResolvedValue({ kind: 'success', data: { ...data, subscriptionStatus: 'active' } });
    await renderControlled();
    const bySummary = (label: string) => [...container.querySelectorAll<HTMLDetailsElement>('details')]
      .find(details => details.querySelector(':scope > summary')?.textContent?.startsWith(label))!;
    const primary = ['Consommation Agent', 'Crédits et paiements', 'État de facturation', 'Détails techniques', 'Outils de réparation']
      .map(bySummary);
    const openPrimary = () => primary.filter(details => details.open);
    const click = async (element: HTMLElement) => {
      await act(async () => {
        element.click();
        await new Promise(resolve => setTimeout(resolve, 0));
      });
    };

    expect(openPrimary()).toHaveLength(0);
    for (const label of ['Consommation Agent', 'Crédits et paiements', 'État de facturation', 'Détails techniques', 'Outils de réparation']) {
      const details = bySummary(label);
      await click(details.querySelector(':scope > summary')!);
      expect(openPrimary()).toEqual([details]);
    }

    await click(bySummary('Outils de réparation').querySelector(':scope > summary')!);
    expect(openPrimary()).toHaveLength(0);

    await click(bySummary('État de facturation').querySelector(':scope > summary')!);
    const machines = bySummary('Machines facturables');
    await click(machines.querySelector(':scope > summary')!);
    expect(machines.open).toBe(true);
    expect(openPrimary()).toEqual([bySummary('État de facturation')]);
  });

  it('shows authorization errors without actions', async () => {
    request.mockResolvedValue({ kind: 'forbidden' }); await render();
    expect(container.textContent).toContain('Accès Super Admin requis'); expect(container.textContent).not.toContain('Créer / récupérer');
  });

});
