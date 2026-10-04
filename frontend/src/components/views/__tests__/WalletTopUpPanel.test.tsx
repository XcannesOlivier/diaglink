import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import {readFileSync} from 'node:fs';
import {resolve} from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { WalletTopUpPanel } from '../WalletTopUpPanel';
import styles from '../CompanyFinancePanel.module.css';

const { read, start } = vi.hoisted(() => ({ read: vi.fn(), start: vi.fn() }));
vi.mock('../../../services/walletTopUpService', () => ({
  getWalletTopUps: read,
  startWalletTopUp: start,
}));

describe('Super Admin wallet consultation', () => {
  let root: Root;
  let box: HTMLDivElement;
  const token = vi.fn();
  const completed = {
    id: 'op-completed', stage: 'Completed', status: 'Completed', amountEur: 20,
    paymentUrl: null, stripeSessionId: 'cs_completed', stripePaymentIntentId: 'pi_completed',
    ledgerEntryId: 'ledger1', externalEventId: 'evt_paid', createdAtUtc: '2026-09-11T12:00:00Z',
    paymentConfirmedAtUtc: '2026-09-11T12:01:00Z', completedAtUtc: '2026-09-11T12:02:00Z',
  };
  const awaiting = {
    ...completed, id: 'op-awaiting', stage: 'AwaitingPayment', status: 'AwaitingPayment', amountEur: 50,
    paymentUrl: 'https://checkout.stripe.com/c/pay/test', stripeSessionId: 'cs_awaiting',
    stripePaymentIntentId: null, ledgerEntryId: null, externalEventId: null,
    paymentConfirmedAtUtc: null, completedAtUtc: null,
  };
  const overview = { balance: 42.5, currency: 'EUR', walletExists: true, enabled: true, operations: [completed, awaiting] };

  beforeEach(() => {
    read.mockReset();
    start.mockReset();
    read.mockResolvedValue({ kind: 'success', data: overview });
    box = document.createElement('div');
    document.body.append(box);
    root = createRoot(box);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    box.remove();
  });

  async function render() {
    await act(async () => root.render(<WalletTopUpPanel companyId="c1" getAccessToken={token} />));
  }

  it('shows the existing recharge history in a collapsed card and toggles a full-width detail', async () => {
    await render();

    const card=box.querySelector<HTMLElement>('[aria-label="Historique des recharges de crédit"]')!;
    const toggle=card.querySelector<HTMLButtonElement>('button')!;
    expect(card.classList.contains(styles.consumptionSummaryCard)).toBe(true);
    expect(toggle.textContent).toBe('Voir l’historique des recharges');expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(box.querySelector('[aria-label="Détail de l’historique des recharges de crédit"]')).toBeNull();
    await act(async()=>toggle.click());
    const history=box.querySelector<HTMLElement>('[aria-label="Détail de l’historique des recharges de crédit"]')!;
    expect(history.classList.contains(styles.topUpHistoryDetail)).toBe(true);
    const css=readFileSync(resolve('src/components/views/CompanyFinancePanel.module.css'),'utf8');
    expect(css).toMatch(/\.topUpHistoryDetail\{grid-column:1\/-1;/);
    expect(css).toMatch(/globalConsumptionMetrics:not\(\.machineConsumptionMetrics\)\{grid-template-columns:minmax\(0,2fr\) repeat\(4,minmax\(0,1fr\)\)/);
    expect(history.textContent).toContain('20,00');expect(history.textContent).toContain('50,00');
    expect(history.textContent).toContain('Payé');expect(history.textContent).toContain('En attente de paiement');
    expect(toggle.textContent).toBe('Masquer l’historique des recharges');expect(toggle.getAttribute('aria-expanded')).toBe('true');
    await act(async()=>toggle.click());expect(box.querySelector('[aria-label="Détail de l’historique des recharges de crédit"]')).toBeNull();

    expect(box.querySelector('input')).toBeNull();
    expect(box.querySelector('a')).toBeNull();
    expect([...box.querySelectorAll('button')].map(button => button.textContent)).toEqual(['Voir l’historique des recharges']);
    expect(box.textContent).not.toContain(new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'EUR' }).format(42.5));
    expect(box.textContent).not.toContain('1 € payé = 1 € de crédits');
    expect(box.textContent).not.toContain('Montant libre en euros');
    expect(box.textContent).not.toContain('Passer au paiement');
    expect(box.textContent).not.toContain('Reprendre la recharge');
    expect(box.textContent).not.toContain('Réessayer la recharge');
    expect(start).not.toHaveBeenCalled();
  });

  it('keeps technical and intervention information read-only', async () => {
    const interventions = vi.fn();
    await act(async () => root.render(<WalletTopUpPanel companyId="c1" getAccessToken={token} onInterventionsChange={interventions} />));

    expect(box.querySelector('section[aria-label="Détails techniques des recharges"]')?.textContent).toContain('cs_completed');
    const repairs = box.querySelector('section[aria-label="Réparation des recharges"]');
    expect(repairs?.textContent).toContain('50,00');
    expect(repairs?.querySelector('button')).toBeNull();
    expect(interventions).toHaveBeenLastCalledWith([expect.objectContaining({
      id: 'op-awaiting', type: 'wallet-top-up', label: expect.stringContaining('En attente de paiement'),
    })]);
    expect(start).not.toHaveBeenCalled();
  });

  it('refreshes consultation data from the shared revision with a read request only', async () => {
    await render();
    await act(async () => root.render(<WalletTopUpPanel companyId="c1" getAccessToken={token} revision={1} />));

    expect(read).toHaveBeenCalledTimes(2);
    expect(read).toHaveBeenLastCalledWith(token, 'c1');
    expect(start).not.toHaveBeenCalled();
  });

  it('shows authorization errors without exposing actions', async () => {
    read.mockResolvedValue({ kind: 'forbidden' });
    await render();

    expect(box.textContent).toContain('Accès Super Admin requis');
    expect(box.querySelector('input')).toBeNull();
    expect(start).not.toHaveBeenCalled();
  });
});
