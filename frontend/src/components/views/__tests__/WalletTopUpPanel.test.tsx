import styles from '../CompanyFinancePanel.module.css';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { WalletTopUpPanel } from '../WalletTopUpPanel';
const { read, start } = vi.hoisted(() => ({ read: vi.fn(), start: vi.fn() }));
vi.mock('../../../services/walletTopUpService', () => ({ getWalletTopUps: read, startWalletTopUp: start }));

describe('Wallet top-up', () => {
  let root: Root; let box: HTMLDivElement;
  const token = vi.fn();
  const overview = { balance: 0, currency: 'EUR', walletExists: false, enabled: true, operations: [] };
  const operation = { id: 'op1', stage: 'AwaitingPayment', status: 'AwaitingPayment', amountEur: 20,
    paymentUrl: 'https://checkout.stripe.com/c/pay/test', stripeSessionId: 'cs_test_local', stripePaymentIntentId: null,
    ledgerEntryId: null, externalEventId: null, createdAtUtc: '2026-09-11T12:00:00Z', paymentConfirmedAtUtc: null, completedAtUtc: null };
  beforeEach(() => { localStorage.clear(); read.mockReset(); start.mockReset(); read.mockResolvedValue({ kind: 'success', data: overview });
    box = document.createElement('div'); document.body.append(box); root = createRoot(box); });
  afterEach(async () => { await act(async () => root.unmount()); box.remove(); localStorage.clear(); });
  async function render() { await act(async () => root.render(<WalletTopUpPanel companyId="c1" getAccessToken={token} />)); }
  function button(text: string) { return [...box.querySelectorAll('button')].find(b => b.textContent === text)!; }
  async function click(text: string) { await act(async () => button(text).click()); }
  async function input(value: string) { await act(async () => {
    const element = box.querySelector('input')!;
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(element, value);
    element.dispatchEvent(new Event('input', { bubbles: true }));
  }); }

  it('shows presets, enforces minimum and cent precision before posting', async () => {
    await render();
    for (const value of [10, 20, 50, 100, 200]) expect(button(`${value} €`)).toBeTruthy();
    expect(button('10 €').getAttribute('aria-pressed')).toBe('true');
    await input('9.99'); expect(button('Passer au paiement').disabled).toBe(true);
    await input('10.001'); expect(button('Passer au paiement').disabled).toBe(true);
    await input('12,34'); expect(button('Passer au paiement').disabled).toBe(false);
    await click('50 €'); expect(box.querySelector('input')!.value).toBe('50'); expect(start).not.toHaveBeenCalled();
  });

  it('credits only on subsequent server confirmation, never on checkout creation', async () => {
    await render(); await click('20 €');
    start.mockResolvedValue({ kind: 'success', data: operation });
    read.mockResolvedValue({ kind: 'success', data: { ...overview, operations: [operation] } });
    await click('Passer au paiement');
    expect(start).toHaveBeenCalledWith(token, 'c1', expect.any(String), 20);
    expect(box.textContent).toContain(new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(0)); expect(box.textContent).toContain('En attente de paiement');
    expect(box.querySelector('a')?.href).toBe(operation.paymentUrl);
    expect(box.querySelector('a')?.target).toBe('_self');
    expect(box.querySelector(`.${styles.topUpAwaiting}`)?.textContent).toBe('En attente de paiement');
    const diagnostic=box.querySelector('section[aria-label="Diagnostic des recharges"]')!;
    const history=[...box.querySelectorAll('details')].find(d=>d.querySelector('summary')?.textContent==='Historique des recharges')!;expect(history.open).toBe(false);expect(box.querySelector('a')?.closest('details')).toBe(history);
    expect(diagnostic.textContent).toContain(operation.stripeSessionId);
    expect(diagnostic.contains(button('Reprendre la recharge'))).toBe(true);
    read.mockResolvedValue({ kind: 'success', data: { ...overview, balance: 20, walletExists: true,
      operations: [{ ...operation, stage: 'Completed', status: 'Completed', paymentUrl: null, ledgerEntryId: 'ledger1', externalEventId: 'evt_paid' }] } });
    await click('Actualiser le wallet');
    expect(box.textContent).toContain(new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR'}).format(20)); expect(box.textContent).toContain('ledger1'); expect(start).toHaveBeenCalledTimes(1);
    expect(box.querySelector('a')).toBeNull();
    expect(box.querySelector(`.${styles.topUpCompleted}`)?.textContent).toBe('Payé');
    start.mockResolvedValue({ kind: 'success', data: { ...operation, status: 'AlreadyCompleted' } });
    await click('Rejouer la recharge sans effet'); expect(start).toHaveBeenLastCalledWith(token, 'c1', 'op1', 20);
  });

  it('keeps the operation key across a lost response and reload, and blocks duplicate clicks', async () => {
    await render(); let resolve!: (v: unknown) => void;
    start.mockImplementationOnce(() => new Promise(r => { resolve = r; }));
    await act(async () => { button('Passer au paiement').click(); button('Passer au paiement').click(); });
    expect(start).toHaveBeenCalledTimes(1); const id = start.mock.calls[0][2];
    expect(JSON.parse(localStorage.getItem('diaglink:wallet-topup:c1')!).id).toBe(id);
    await act(async () => resolve({ kind: 'error' }));
    await act(async () => root.unmount()); root = createRoot(box); await render();
    start.mockResolvedValue({ kind: 'success', data: { ...operation, id } });
    await click('Réessayer la recharge'); expect(start).toHaveBeenLastCalledWith(token, 'c1', id, 10);
    expect(localStorage.getItem('diaglink:wallet-topup:c1')).toBeNull();
  });

  it('fails closed for disabled configuration, forbidden access and untrusted payment URLs', async () => {
    read.mockResolvedValue({ kind: 'success', data: { ...overview, enabled: false, operations: [{ ...operation, paymentUrl: 'https://evil.example/pay' }] } });
    await render(); expect(button('Passer au paiement').disabled).toBe(true); expect(box.querySelector('a')).toBeNull();
    read.mockResolvedValue({ kind: 'forbidden' }); await click('Actualiser le wallet');
    expect(box.textContent).toContain('Accès Super Admin requis'); expect(start).not.toHaveBeenCalled();
  });
});
