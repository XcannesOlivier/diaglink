import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { StripeSubscriptionPaymentPanel } from '../StripeSubscriptionPaymentPanel';
const { read } = vi.hoisted(() => ({ read: vi.fn() }));
vi.mock('../../../services/stripeAdminService', () => ({ stripeSubscriptionPaymentRequest: read }));
describe('Subscription payment', () => {
  let root: Root; let box: HTMLDivElement;
  const token = vi.fn();
  const invoice = { id: 'in_test', status: 'open', billingReason: 'subscription_create', quantity: 2,
    amountPaidCents: 0, paymentUrl: 'https://invoice.stripe.com/i/test', startUtc: '2026-09-14', endUtc: '2026-10-14' };
  beforeEach(() => { read.mockReset(); box=document.createElement('div'); document.body.append(box); root=createRoot(box); });
  afterEach(async () => { await act(async () => root.unmount()); box.remove(); });
  async function render() { await act(async () => root.render(<StripeSubscriptionPaymentPanel companyId="company" getAccessToken={token} />)); }
  async function refresh() { await act(async () => box.querySelector('button')!.click()); }
  it('reads on demand and opens only the unpaid hosted invoice in the same tab', async () => {
    await render(); expect(read).not.toHaveBeenCalled();
    read.mockResolvedValue({kind:'success',data:{invoice,payments:[]}}); await refresh();
    expect(read).toHaveBeenCalledWith(token,'company');
    expect(box.textContent).toContain('59,80'); expect(box.querySelector('a')?.target).toBe('_self');
    expect(box.querySelector('a')?.href).toBe(invoice.paymentUrl);
    read.mockResolvedValue({kind:'success',data:{invoice:{...invoice,status:'paid',amountPaidCents:5980},
      payments:[{stripeInvoiceId:'in_test',billingReason:'subscription_cycle',status:'Completed',periodStartUtc:invoice.startUtc,periodEndUtc:invoice.endUtc}]}});
    await refresh(); expect(box.querySelector('a')).toBeNull(); expect(box.textContent).toContain('Completed');
    expect(box.textContent).toContain('subscription_cycle');
  });
  it('does not link untrusted URLs or claim credits after a read error', async () => {
    await render(); read.mockResolvedValue({kind:'success',data:{invoice:{...invoice,paymentUrl:'https://evil.example/'},payments:[]}});
    await refresh(); expect(box.querySelector('a')).toBeNull();
    read.mockResolvedValue({kind:'forbidden'}); await refresh();
    expect(box.querySelector('[role="alert"]')).not.toBeNull(); expect(box.textContent).not.toContain('in_test');
  });
});
