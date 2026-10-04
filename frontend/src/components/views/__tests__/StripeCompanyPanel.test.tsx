import { createPortal } from 'react-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, useEffect, useState, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { StripeCompanyPanel } from '../StripeCompanyPanel';
import type { AdminSection } from '../adminAccordion';
import styles from '../CompanyFinancePanel.module.css';

const { request, walletRows, machineRows } = vi.hoisted(() => ({
  request: vi.fn(),
  walletRows: { current: [] as Array<{ id: string; type: 'wallet-top-up'; label: string }> },
  machineRows: { current: [] as Array<{ id: string; type: 'machine-addition'; machineId: string; label: string }> },
}));

vi.mock('../../../services/stripeAdminService', () => ({ stripeCompanyRequest: request }));
vi.mock('../StripeMachineAdditionsPanel', () => ({
  StripeMachineAdditionsPanel: ({ onInterventionsChange }: { onInterventionsChange?: (rows: typeof machineRows.current) => void }) => {
    useEffect(() => onInterventionsChange?.(machineRows.current), [onInterventionsChange]);
    return null;
  },
}));
vi.mock('../WalletTopUpPanel', () => ({
  WalletTopUpPanel: ({ technicalTarget, repairTarget, onInterventionsChange, onLoadComplete }: {
    technicalTarget?: HTMLElement | null;
    repairTarget?: HTMLElement | null;
    onInterventionsChange?: (rows: typeof walletRows.current) => void;
    onLoadComplete?: () => void;
  }) => {
    useEffect(() => { onInterventionsChange?.(walletRows.current); onLoadComplete?.(); }, [onInterventionsChange, onLoadComplete]);
    return <>
      {technicalTarget && createPortal(<section aria-label="Détails wallet" />, technicalTarget)}
      {repairTarget && createPortal(<section aria-label="Réparation wallet" />, repairTarget)}
      <article aria-label="Historique des recharges de crédit">Historique des recharges de crédit</article>
    </>;
  },
}));
vi.mock('../CompanyConsumptionPanel', () => ({
  CompanyConsumptionPanel: ({ view, globalContent, machineInterventions = [], onExamineInterventions }: {
    view: 'global' | 'machines';
    globalContent?: ReactNode;
    machineInterventions?: typeof machineRows.current;
    onExamineInterventions?: () => void;
  }) => <section aria-label={view === 'global' ? 'Consommation globale' : 'Consommation par machine'}>
    {view === 'global'
      ? <div data-global-cards><article>Quota inclus</article><article>Crédit supplémentaire consommé</article>{globalContent}</div>
      : machineInterventions.map(intervention => <article key={intervention.id} data-machine-intervention={intervention.machineId}>{intervention.label}<button onClick={onExamineInterventions}>Examiner / reprendre</button></article>)}
  </section>,
}));

describe('StripeCompanyPanel', () => {
  let root: Root;
  let container: HTMLDivElement;
  const token = vi.fn();
  const data = {
    stripeCustomerId: null, stripeSubscriptionId: null, subscriptionStatus: null,
    currentPeriodStartUtc: null, currentPeriodEndUtc: null, activeMachineCount: 2, testActionsEnabled: true,
    machines: [{ id: 'm1', name: 'Machine A', billable: true, rightsEndUtc: null }, { id: 'm2', name: 'Machine B', billable: true, rightsEndUtc: null }],
  };

  beforeEach(() => {
    request.mockReset();
    walletRows.current = [];
    machineRows.current = [];
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);
  });
  afterEach(async () => { await act(async () => root.unmount()); container.remove(); });

  async function render(refreshRevision = 0, onRefreshComplete?: (revision: number) => void) {
    await act(async () => root.render(<StripeCompanyPanel companyId="c1" getAccessToken={token} refreshRevision={refreshRevision} onRefreshComplete={onRefreshComplete} />));
  }
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

  it('charge les données sans mutation et suit la révision partagée', async () => {
    request.mockResolvedValueOnce({ kind: 'success', data });
    await render();
    expect(request).toHaveBeenCalledExactlyOnceWith(token, 'c1');
    expect(container.textContent).toContain('Machines actives');
    expect(container.textContent).not.toContain('Créer / récupérer');
    request.mockResolvedValueOnce({ kind: 'success', data: { ...data, stripeCustomerId: 'cus_test' } });
    await render(1);
    expect(request).toHaveBeenLastCalledWith(token, 'c1');
    expect(container.textContent).toContain('cus_test');
    expect([...container.querySelectorAll('button')].some(button => button.textContent?.startsWith('Actualiser'))).toBe(false);
  });

  it('supprime la facturation autonome et affiche les cinq cartes globales', async () => {
    request.mockResolvedValue({ kind: 'success', data });
    await render();
    expect([...container.querySelectorAll('summary')].map(summary => summary.textContent)).toEqual([
      'Consommation globale', 'Consommation par machine', 'Détails techniques', 'Outils de réparation',
    ]);
    expect(container.querySelectorAll('[data-global-cards] > article')).toHaveLength(5);
    expect(container.textContent).toContain('Historique des recharges de crédit');
    expect(container.textContent).toContain('Montant en attente');
    expect(container.textContent).toContain('Opérations nécessitant une intervention');
    expect(container.textContent).not.toContain('État de facturation');
    expect(container.textContent).not.toContain('Machines facturables');
    expect(container.querySelectorAll('[aria-label="Résumé de l’entreprise"]')).toHaveLength(1);
  });

  it('réutilise le montant en attente existant dans la nouvelle carte', async () => {
    request.mockResolvedValue({ kind: 'success', data: { ...data, subscriptionStatus: 'active', amountRemainingCents: 1250 } });
    await render();
    const card = container.querySelector('[aria-label="Montant en attente"]')!;
    expect(card.textContent?.replace(/\u00a0/g, ' ')).toContain('12,50 €');
    expect(container.querySelector('[aria-label="Opérations nécessitant une intervention"]')?.textContent).toContain('Aucune');
    expect(container.textContent).not.toContain('Synchronisé avec Stripe');
  });

  it('laisse uniquement les détails techniques et réparations repliés par défaut', async () => {
    request.mockResolvedValue({ kind: 'success', data: { ...data, stripeCustomerId: 'cus_test' } });
    await render();
    const sections = [...container.querySelectorAll('details')].filter(details => ['Détails techniques', 'Outils de réparation'].includes(details.querySelector('summary')?.textContent ?? ''));
    expect(sections).toHaveLength(2);
    expect(sections.every(details => !details.open)).toBe(true);
    expect(sections[1].classList.contains(styles.repairTools)).toBe(false);
    expect(container.textContent).toContain('cus_test');
  });

  it('rattache les ajouts à leur machine et garde les recharges globales', async () => {
    walletRows.current = [{ id: 'wallet-1', type: 'wallet-top-up', label: 'Recharge de 25,00 € — En attente' }];
    machineRows.current = [{ id: 'addition-1', type: 'machine-addition', machineId: 'm1', label: 'Ajout à reprendre' }];
    request.mockResolvedValue({ kind: 'success', data });
    await render();

    expect(container.querySelector('[aria-label="Opérations nécessitant une intervention"] strong')?.textContent).toBe('2');
    const machine = container.querySelector('[data-machine-intervention="m1"]')!;
    expect(machine.textContent).toContain('Ajout à reprendre');
    expect(machine.textContent).not.toContain('Recharge de 25,00 €');

    const globalCard = container.querySelector('[aria-label="Opérations nécessitant une intervention"]')!;
    await act(async () => (globalCard.querySelector('button') as HTMLButtonElement).click());
    const globalDetail = container.querySelector('[aria-label="Détail des interventions globales"]')!;
    expect(globalDetail.textContent).toContain('Recharge de 25,00 €');
    expect(globalDetail.textContent).not.toContain('Ajout à reprendre');
    await act(async () => (globalDetail.querySelector('button') as HTMLButtonElement).click());
    expect([...container.querySelectorAll('details')].find(details => details.querySelector('summary')?.textContent === 'Outils de réparation (2)')?.open).toBe(true);
  });

  it('garde les quatre accordéons financiers mutuellement exclusifs', async () => {
    request.mockResolvedValue({ kind: 'success', data: { ...data, subscriptionStatus: 'active' } });
    await renderControlled();
    const bySummary = (label: string) => [...container.querySelectorAll<HTMLDetailsElement>('details')]
      .find(details => details.querySelector(':scope > summary')?.textContent?.startsWith(label))!;
    const labels = ['Consommation globale', 'Consommation par machine', 'Détails techniques', 'Outils de réparation'];
    const primary = labels.map(bySummary);
    const openPrimary = () => primary.filter(details => details.open);
    const click = async (element: HTMLElement) => act(async () => { element.click(); await new Promise(resolve => setTimeout(resolve, 0)); });

    expect(openPrimary()).toHaveLength(0);
    for (const label of labels) {
      const details = bySummary(label);
      await click(details.querySelector(':scope > summary')!);
      expect(openPrimary()).toEqual([details]);
    }
    await click(bySummary('Outils de réparation').querySelector(':scope > summary')!);
    expect(openPrimary()).toHaveLength(0);
  });

  it('affiche les erreurs d’autorisation sans action', async () => {
    request.mockResolvedValue({ kind: 'forbidden' });
    await render();
    expect(container.textContent).toContain('Accès Super Admin requis');
    expect(container.textContent).not.toContain('Créer / récupérer');
  });
});
