import { it, expect, vi } from 'vitest';
import { act, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';
import { AdminFinanceOverview } from '../AdminFinanceOverview';

vi.mock('../CompanyConsumptionPanel', () => ({
  CompanyConsumptionPanel: ({ view, globalContent }: { view: 'global' | 'machines'; globalContent?: ReactNode }) => (
    <section aria-label={view === 'global' ? 'Consommation globale' : 'Consommation par machine'}>
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
