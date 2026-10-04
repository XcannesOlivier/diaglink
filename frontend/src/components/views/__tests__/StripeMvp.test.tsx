import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { StripeAdminPanel } from '../StripeAdminPanel';
import type { StripeAdditionSummary, StripeCompanySummary } from '../../../services/stripeAdminService';
import styles from '../CompanyFinancePanel.module.css';

const mocks = vi.hoisted(() => ({ companies: vi.fn(), account: vi.fn(), operations: vi.fn(), add: vi.fn() }));
vi.mock('../../../services/companyService', () => ({ getCompanies: mocks.companies }));
vi.mock('../WalletTopUpPanel', () => ({ WalletTopUpPanel: () => null }));
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

  it('shows only incomplete historical additions as repair actions', async () => {
    mocks.operations.mockResolvedValue({ kind: 'success', data: [op] });
    await render(); expect(mocks.account).toHaveBeenCalledWith(token, 'c1'); expect(mocks.add).not.toHaveBeenCalled();
    expect(container.textContent).not.toContain('Lancer l’ajout Stripe');
    const repairs = [...container.querySelectorAll('details')].find(details => details.querySelector('summary')?.textContent === 'Outils de réparation (1)')!;
    expect(container.textContent).not.toContain('État de facturation');
    expect(container.textContent).not.toContain('Machines facturables');
    expect(container.querySelector('[aria-label="Opérations nécessitant une intervention"] strong')?.textContent).toBe('1');
    expect(repairs.open).toBe(false);
    expect(repairs.classList.contains(styles.repairTools)).toBe(true);
    await act(async () => repairs.querySelector('summary')!.click());
    expect(repairs.open).toBe(true);
    mocks.add.mockResolvedValue({ kind: 'success', data: { operationId: 'op1', status: 'AwaitingPayment' } });
    await click('Reprendre l’opération');
    expect(mocks.add).toHaveBeenCalledExactlyOnceWith(token, 'c1', 'm1');
    expect(container.textContent).toContain('AwaitingPayment'); expect(container.textContent).toContain('19.95 €');
    expect(container.textContent).not.toContain('period1');
    mocks.operations.mockResolvedValue({ kind: 'success', data: [{ ...op, stage: 'Completed', machineBillingPeriodId: 'period1',
      paymentConfirmedAtUtc: '2026-09-15T12:01:00Z', externalEventId: 'evt_test', completedAtUtc: '2026-09-15T12:01:01Z' }] });
    await click('Actualiser les opérations');
    expect(mocks.add).toHaveBeenCalledTimes(1); expect(container.textContent).toContain('Completed'); expect(container.textContent).toContain('period1');
    expect([...container.querySelectorAll('button')].some(button => button.textContent?.includes('Reprendre l’opération'))).toBe(false);
    expect(container.textContent).toContain('Aucun ajout de machine ne nécessite d’intervention.');
    expect(container.querySelector('[aria-label="Opérations nécessitant une intervention"] strong')?.textContent).toBe('Aucune');
    expect(repairs.querySelector('summary')?.textContent).toBe('Outils de réparation');
    expect(repairs.classList.contains(styles.repairTools)).toBe(false);
  });

  it('retries the same incomplete addition after an uncertain response and blocks double clicks', async () => {
    mocks.account.mockResolvedValue({ kind: 'success', data: { ...empty, subscriptionStatus: 'active' } });
    mocks.operations.mockResolvedValue({ kind: 'success', data: [{ ...op, stage: 'StripeQuantityUpdated' }] });
    await render();
    let resolve!: (value: unknown) => void;
    mocks.add.mockImplementationOnce(() => new Promise(r => { resolve = r; }));
    await act(async () => { button('Reprendre l’opération').click(); button('Reprendre l’opération').click(); });
    expect(mocks.add).toHaveBeenCalledTimes(1);
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

  it('requires manual verification instead of replaying a reconciliation operation', async () => {
    mocks.account.mockResolvedValue({ kind: 'success', data: { ...empty, testActionsEnabled: false } });
    mocks.operations.mockResolvedValue({ kind: 'success', data: [{ ...op, reconciliationRequired: true }] });
    await render();
    expect(container.textContent).not.toContain('Lancer l’ajout Stripe');
    expect([...container.querySelectorAll('button')].some(item => item.textContent === 'Reprendre l’opération')).toBe(false);
    expect(container.textContent).toContain('ReconciliationRequired'); expect(mocks.add).not.toHaveBeenCalled();
  });
});
