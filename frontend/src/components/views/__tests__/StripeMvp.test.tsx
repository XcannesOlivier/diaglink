import {createPortal} from 'react-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { StripeAdminPanel } from '../StripeAdminPanel';
import type { StripeAdditionSummary, StripeCompanySummary } from '../../../services/stripeAdminService';

const mocks = vi.hoisted(() => ({ companies: vi.fn(), account: vi.fn(), operations: vi.fn(), add: vi.fn() }));
vi.mock('../../../services/companyService', () => ({ getCompanies: mocks.companies }));
vi.mock('../WalletTopUpPanel', () => ({ WalletTopUpPanel: ({diagnosticContent,diagnosticTarget}: {diagnosticContent?: import('react').ReactNode;diagnosticTarget?:HTMLElement|null}) => diagnosticTarget?createPortal(<details><summary>Outils Stripe et diagnostic</summary>{diagnosticContent}</details>,diagnosticTarget):null }));
vi.mock('../../../services/stripeAdminService', () => ({ stripeCompanyRequest: mocks.account,
  getStripeAdditions: mocks.operations, addStripeMachine: mocks.add }));

describe('Stripe MVP administration', () => {
  let root: Root; let container: HTMLDivElement;
  const token = vi.fn();
  const empty: StripeCompanySummary = { billingAccountId: null, stripeCustomerId: null, stripeSubscriptionId: null,
    machineRequestProvisioningCompleted: false,
    subscriptionStatus: null, currentPeriodStartUtc: null, currentPeriodEndUtc: null, activeMachineCount: 1,
    activeMachines: [{ id: 'm1', name: 'Machine test', hasBillingPeriod: false }], testActionsEnabled: true };
  const op: StripeAdditionSummary = { id: 'op1', machineId: 'm1', machineName: 'Machine test', stage: 'AwaitingPayment',
    stripeInvoiceId: 'in_test', targetQuantity: 2, aiAmountEur: 10, serviceAmountEur: 9.95,
    activatedAtUtc: '2026-09-15T12:00:00Z', cycleStartUtc: '2026-09-01T12:00:00Z', cycleEndUtc: '2026-10-01T12:00:00Z',
    paymentConfirmedAtUtc: null, machineBillingPeriodId: null, externalEventId: null, completedAtUtc: null, reconciliationRequired: false };
  beforeEach(() => {
    Object.values(mocks).forEach(m => m.mockReset());
    mocks.companies.mockResolvedValue({ kind: 'success', data: [{ id: 'c1', name: 'Entreprise test', status: 'active' },
      { id: 'c2', name: 'Autre entreprise', status: 'active' }] });
    mocks.account.mockResolvedValue({ kind: 'success', data: empty });
    mocks.operations.mockResolvedValue({ kind: 'success', data: [] });
    container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  });
  afterEach(async () => { await act(async () => root.unmount()); container.remove(); });
  async function render() {
    await act(async () => root.render(<StripeAdminPanel getAccessToken={token} />));
    await act(async () => { const select = container.querySelector('select')!; select.value = 'c1'; select.dispatchEvent(new Event('change', { bubbles: true })); });
  }
  function button(text: string) { return [...container.querySelectorAll('button')].find(b => b.textContent === text)!; }
  async function click(text: string) { await act(async () => button(text).click()); }

  it('runs Customer, Subscription, AwaitingPayment, read-only webhook refresh, Completed and replay', async () => {
    await render(); expect(mocks.account).toHaveBeenCalledWith(token, 'c1'); expect(mocks.add).not.toHaveBeenCalled();
    mocks.account.mockResolvedValue({ kind: 'success', data: { ...empty, billingAccountId: 'ba1', stripeCustomerId: 'cus_test' } });
    await click('Créer / récupérer le client Stripe'); expect(container.textContent).toContain('cus_test'); expect(container.textContent).toContain('ba1');
    mocks.account.mockResolvedValue({ kind: 'success', data: { ...empty, billingAccountId: 'ba1', stripeCustomerId: 'cus_test',
      stripeSubscriptionId: 'sub_test', subscriptionStatus: 'active', currentPeriodEndUtc: op.cycleEndUtc } });
    await click('Créer / récupérer l’abonnement Stripe'); expect(mocks.account).toHaveBeenLastCalledWith(token, 'c1', 'subscription');
    mocks.add.mockResolvedValue({ kind: 'success', data: { operationId: 'op1', status: 'AwaitingPayment' } });
    mocks.operations.mockResolvedValue({ kind: 'success', data: [op] });
    await click('Lancer l’ajout Stripe');
    expect(mocks.add).toHaveBeenCalledExactlyOnceWith(token, 'c1', 'm1');
    expect(container.textContent).toContain('AwaitingPayment'); expect(container.textContent).toContain('19.95 €');
    expect(container.textContent).not.toContain('period1');
    mocks.operations.mockResolvedValue({ kind: 'success', data: [{ ...op, stage: 'Completed', machineBillingPeriodId: 'period1',
      paymentConfirmedAtUtc: '2026-09-15T12:01:00Z', externalEventId: 'evt_test', completedAtUtc: '2026-09-15T12:01:01Z' }] });
    await click('Actualiser les opérations');
    expect(mocks.add).toHaveBeenCalledTimes(1); expect(container.textContent).toContain('Completed'); expect(container.textContent).toContain('period1');
    mocks.add.mockResolvedValue({ kind: 'success', data: { operationId: 'op1', status: 'AlreadyCompleted' } });
    const completedButton = [...container.querySelectorAll('button')].find(b => b.textContent === 'Rejouer sans effet')!;
    expect(completedButton.disabled).toBe(true);
    await click('Rejouer sans effet'); expect(mocks.add).toHaveBeenCalledTimes(1);
  });

  it('retries the same machine after an uncertain response and blocks double clicks', async () => {
    mocks.account.mockResolvedValue({ kind: 'success', data: { ...empty, subscriptionStatus: 'active' } });
    await render();
    let resolve!: (value: unknown) => void;
    mocks.add.mockImplementationOnce(() => new Promise(r => { resolve = r; }));
    await act(async () => { button('Lancer l’ajout Stripe').click(); button('Lancer l’ajout Stripe').click(); });
    expect(mocks.add).toHaveBeenCalledTimes(1);
    mocks.operations.mockResolvedValue({ kind: 'success', data: [{ ...op, stage: 'StripeQuantityUpdated' }] });
    await act(async () => resolve({ kind: 'error' }));
    mocks.add.mockResolvedValue({ kind: 'success', data: { operationId: 'op1', status: 'AwaitingPayment' } });
    await click('Reprendre l’opération'); expect(mocks.add).toHaveBeenLastCalledWith(token, 'c1', 'm1');
  });

  it('does not expose another company state on selection change', async () => {
    mocks.account.mockResolvedValueOnce({ kind: 'success', data: { ...empty, stripeCustomerId: 'cus_first' } });
    await render(); expect(container.textContent).toContain('cus_first');
    await act(async () => { const select = container.querySelector('select')!; select.value = 'c2'; select.dispatchEvent(new Event('change', { bubbles: true })); });
    expect(container.textContent).not.toContain('cus_first'); expect(mocks.account).toHaveBeenLastCalledWith(token, 'c2');
  });

  it('disables mutation for live/disabled config, nonactive subscription and expired operation', async () => {
    mocks.account.mockResolvedValue({ kind: 'success', data: { ...empty, testActionsEnabled: false } });
    mocks.operations.mockResolvedValue({ kind: 'success', data: [{ ...op, reconciliationRequired: true }] });
    await render();
    expect(button('Lancer l’ajout Stripe').disabled).toBe(true); expect(button('Reprendre l’opération').disabled).toBe(true);
    expect(container.textContent).toContain('ReconciliationRequired'); expect(mocks.add).not.toHaveBeenCalled();
  });
});
