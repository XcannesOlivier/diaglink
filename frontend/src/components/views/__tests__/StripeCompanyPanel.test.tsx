import {createPortal} from 'react-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { StripeCompanyPanel } from '../StripeCompanyPanel';
const { request } = vi.hoisted(() => ({ request: vi.fn() }));
vi.mock('../../../services/stripeAdminService', () => ({ stripeCompanyRequest: request }));
vi.mock('../StripeMachineAdditionsPanel', () => ({ StripeMachineAdditionsPanel: () => null }));
vi.mock('../WalletTopUpPanel', () => ({ WalletTopUpPanel: ({diagnosticContent,diagnosticTarget}: {diagnosticContent?: import('react').ReactNode;diagnosticTarget?:HTMLElement|null}) => diagnosticTarget?createPortal(<section aria-label="Diagnostic wallet">{diagnosticContent}</section>,diagnosticTarget):<span>Crédit supplémentaire</span> }));

describe('StripeCompanyPanel', () => {
  let root: Root;
  let container: HTMLDivElement;
  const token = vi.fn();
  const data = { stripeCustomerId: null, stripeSubscriptionId: null, subscriptionStatus: null,
    currentPeriodStartUtc: null, currentPeriodEndUtc: null, activeMachineCount: 2, testActionsEnabled: true };
  beforeEach(() => { request.mockReset(); container = document.createElement('div'); document.body.append(container); root = createRoot(container); });
  afterEach(async () => { await act(async () => root.unmount()); container.remove(); });
  async function render() { await act(async () => root.render(<StripeCompanyPanel companyId="c1" getAccessToken={token} />)); }

  it('loads without mutations and updates after explicit action; prevents double clicks', async () => {
    request.mockResolvedValueOnce({ kind: 'success', data }); await render();
    expect(request).toHaveBeenCalledExactlyOnceWith(token, 'c1');
    expect(container.textContent).toContain('Machines actives');
    let resolve!: (value: unknown) => void;
    request.mockImplementationOnce(() => new Promise(r => { resolve = r; }));
    const button = [...container.querySelectorAll('button')].find(b => b.textContent?.includes('le client Stripe'))!;
    await act(async () => { button.click(); button.click(); });
    expect(request).toHaveBeenCalledTimes(2); expect(button.disabled).toBe(true);
    await act(async () => resolve({ kind: 'success', data: { ...data, stripeCustomerId: 'cus_test' } }));
    expect(container.textContent).toContain('cus_test'); expect(button.disabled).toBe(false);
    request.mockResolvedValueOnce({ kind: 'success', data: { ...data, stripeSubscriptionId: 'sub_test', subscriptionStatus: 'active',
      currentPeriodStartUtc: '2026-09-11T00:00:00Z', currentPeriodEndUtc: '2026-10-11T00:00:00Z' } });
    await act(async () => [...container.querySelectorAll('button')].find(b => b.textContent?.includes('l’abonnement Stripe'))!.click());
    expect(request).toHaveBeenLastCalledWith(token, 'c1', 'subscription');
    expect(container.textContent).toContain('sub_test'); expect(container.textContent).toContain('2026-10-11');
  });

  it('orders company sections without repeated summary cards', async () => {
    request.mockResolvedValue({ kind: 'success', data }); await render();
    expect([...container.querySelectorAll('summary')].map(s => s.textContent)).toEqual([
      'Consommation Agent', 'Crédits et paiements', 'Diagnostic Super Admin'
    ]);
    expect(container.querySelectorAll('[aria-label="Résumé de l’entreprise"]')).toHaveLength(1);
    expect(container.textContent).not.toContain('Statut et consommation des machines');
    expect(container.textContent).not.toContain('Consommation IA globale de l’entreprise');
    expect(container.textContent).not.toContain('Crédit supplémentaire entreprise');
  });

  it('disables actions when test configuration is unavailable', async () => {
    request.mockResolvedValue({ kind: 'success', data: { ...data, testActionsEnabled: false } }); await render();
    expect([...container.querySelectorAll('button')].filter(b => b.textContent?.startsWith('Créer')).every(b => b.disabled)).toBe(true);
  });

  it('shows authorization errors without actions', async () => {
    request.mockResolvedValue({ kind: 'forbidden' }); await render();
    expect(container.textContent).toContain('Accès Super Admin requis'); expect(container.textContent).not.toContain('Créer / récupérer');
  });

  it('shows a conflict without claiming success', async () => {
    request.mockResolvedValueOnce({ kind: 'success', data }); await render();
    request.mockResolvedValueOnce({ kind: 'conflict', message: 'Réconciliation requise' });
    await act(async () => [...container.querySelectorAll('button')].find(b => b.textContent?.includes('le client Stripe'))!.click());
    expect(container.textContent).toContain('Réconciliation requise');
  });
});
