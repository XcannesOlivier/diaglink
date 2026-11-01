import { it, expect, vi } from 'vitest';
import { act, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';
import { AdminFinanceOverview } from '../AdminFinanceOverview';

vi.mock('../CompanyConsumptionPanel', () => ({
  CompanyConsumptionPanel: ({ view, active, globalContent }: { view: 'global' | 'machines'; active?: boolean; globalContent?: ReactNode }) => (
    <section aria-label={view === 'global' ? 'Consommation globale' : 'Consommation par machine'} data-active={String(active)}>
      {view === 'global' && <div data-global-cards><article>Quota inclus</article><article>Crédit consommé</article>{globalContent}</div>}
    </section>
  ),
}));

it('supprime le bloc de facturation et place les cinq cartes dans la consommation globale', async () => {
  const host = document.createElement('div');
  const root = createRoot(host);
  try {
    await act(async () => root.render(
      <AdminFinanceOverview
        companyId="c"
        token={async () => null}
        globalContent={<><article>Historique des recharges de crédit</article><article>Montant en attente</article><article>Opérations nécessitant une intervention</article></>}
      />,
    ));
    expect([...host.querySelectorAll('summary')].map(summary => summary.textContent)).toEqual(['Consommation globale', 'Consommation par machine']);
    expect(host.querySelectorAll('[data-global-cards] > article')).toHaveLength(5);
    expect(host.textContent).toContain('Historique des recharges de crédit');
    expect(host.textContent).toContain('Montant en attente');
    expect(host.textContent).toContain('Opérations nécessitant une intervention');
    expect(host.textContent).not.toContain('État de facturation');
    expect(host.textContent).not.toContain('Machines facturables');
    expect(host.textContent).not.toContain('Consommation Agent');
    expect(host.textContent).not.toContain('Crédits et paiements');
  } finally {
    await act(async () => root.unmount());
  }
});

it('active uniquement le rapport correspondant au panneau ouvert', async () => {
  const host = document.createElement('div');
  const root = createRoot(host);
  try {
    await act(async () => root.render(<AdminFinanceOverview companyId="c" token={async () => null} />));
    const details = [...host.querySelectorAll<HTMLDetailsElement>('details')];
    const reports = () => [...host.querySelectorAll<HTMLElement>('section[data-active]')];
    expect(details.map(detail => detail.open)).toEqual([false, false]);
    expect(reports().map(report => report.dataset.active)).toEqual(['false', 'false']);

    await act(async () => {
      details[0].open = true;
      details[0].dispatchEvent(new Event('toggle'));
    });
    expect(details.map(detail => detail.open)).toEqual([true, false]);
    expect(reports().map(report => report.dataset.active)).toEqual(['true', 'false']);

    await act(async () => {
      details[1].open = true;
      details[1].dispatchEvent(new Event('toggle'));
    });
    expect(details.map(detail => detail.open)).toEqual([false, true]);
    expect(reports().map(report => report.dataset.active)).toEqual(['false', 'true']);

    await act(async () => {
      details[1].open = false;
      details[1].dispatchEvent(new Event('toggle'));
    });
    expect(details.map(detail => detail.open)).toEqual([false, false]);
    expect(reports().map(report => report.dataset.active)).toEqual(['false', 'false']);
  } finally {
    await act(async () => root.unmount());
  }
});
