import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import type { StripeCompanySummary } from '../../../services/stripeAdminService';
import { CompanySummaryBanner } from '../CompanySummaryBanner';

vi.mock('../../../utils/apiAuth', () => ({ getApiAuthHeaders: async () => ({ headers: {} }) }));
afterEach(() => vi.unstubAllGlobals());

async function renderSummary(account: Partial<StripeCompanySummary>, walletBalance = 20) {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ walletBalance })));
  vi.stubGlobal('fetch', fetch);
  const host = document.createElement('div');
  const root = createRoot(host);
  await act(async () => root.render(
    <CompanySummaryBanner companyId="a" account={account as StripeCompanySummary} token={async () => null} revision={0} />,
  ));
  return { host, root, fetch };
}

it('displays explicit machine, wallet and active subscription badges from existing values', async () => {
  const { host, root, fetch } = await renderSummary({
    activeMachineCount: 2,
  });
  try {
    expect(host.querySelectorAll('dt')).toHaveLength(3);
    expect([...host.querySelectorAll('dd')].map(element => element.textContent?.replace(/\s/g, ' '))).toEqual([
      '2 machines',
      'Crédit supplémentaire : 20,00 €',
      '2 abonnements actifs',
    ]);
    expect([...host.querySelectorAll('dt')].map(element => element.textContent)).toEqual([
      'Machines', 'Crédit supplémentaire', 'Abonnements actifs',
    ]);
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(fetch.mock.calls[0][0]).toContain('/companies/a/stripe/finance');
  } finally {
    await act(async () => root.unmount());
  }
});

it.each([
  [0, '0 machine', '0 abonnement actif'],
  [1, '1 machine', '1 abonnement actif'],
  [2, '2 machines', '2 abonnements actifs'],
] as const)('uses the correct singular or plural for a count of %s', async (count, machinesLabel, subscriptionsLabel) => {
  const machines = Array.from({ length: count }, (_, index) => ({
    id: `m${index}`, name: `Machine ${index}`, billable: true, rightsEndUtc: null,
  }));
  const { host, root } = await renderSummary({ activeMachineCount: count, machines }, 0);
  try {
    const badges = [...host.querySelectorAll('dd')].map(element => element.textContent?.replace(/\s/g, ' '));
    expect(badges[0]).toBe(machinesLabel);
    expect(badges[1]).toBe('Crédit supplémentaire : 0,00 €');
    expect(badges[2]).toBe(subscriptionsLabel);
  } finally {
    await act(async () => root.unmount());
  }
});
