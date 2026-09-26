import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AdminAccordionControl, AdminSection } from '../adminAccordion';
import { DiagLinkAdminView } from '../DiagLinkAdminView';

vi.mock('../../../hooks/useAuth', () => ({ useAuth: () => ({ getAccessToken: vi.fn() }) }));
vi.mock('../GlobalFinancePeriod', () => ({ GlobalFinancePeriod: () => null }));
vi.mock('../StripeAdminPanel', () => ({
  StripeAdminPanel: ({ accordion }: { accordion: AdminAccordionControl }) => (
    <section aria-label="Sections financières de test">
      {([
        ['consumption', 'Consommation Agent'],
        ['payments', 'Crédits et paiements'],
        ['billing', 'État de facturation'],
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
          {section === 'billing' && <details data-internal-section><summary>Machines facturables</summary></details>}
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

  it('allows zero or one primary section to be open while nested sections remain independent', async () => {
    await act(async () => root.render(<DiagLinkAdminView />));
    const statistics = container.querySelector<HTMLButtonElement>('button[aria-controls]')!;

    expect(statistics.getAttribute('aria-expanded')).toBe('false');
    expect(openPrimarySections()).toEqual([]);

    await click(summary('consumption'));
    expect(openPrimarySections()).toEqual(['consumption']);

    await click(summary('payments'));
    expect(openPrimarySections()).toEqual(['payments']);

    await click(summary('billing'));
    expect(openPrimarySections()).toEqual(['billing']);

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

    await click(summary('billing'));
    const nested = container.querySelector<HTMLDetailsElement>('[data-internal-section]')!;
    await click(nested.querySelector('summary')!);
    expect(nested.open).toBe(true);
    expect(openPrimarySections()).toEqual(['billing']);
  });
});