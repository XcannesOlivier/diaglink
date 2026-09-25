import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Navigation } from '../../layout/Navigation';
import type { MachineRequestDetail, MachineRequestListItem } from '../../../services/machineRequestAdminApi';
import { MachineRequestsView } from '../MachineRequestsView';

const api = vi.hoisted(() => ({ get: vi.fn(), download: vi.fn(), update: vi.fn(), capture: vi.fn(), cancel: vi.fn(), decide: vi.fn(), decideDocuments: vi.fn(), attach: vi.fn(), linkCustomer: vi.fn(), subscription: vi.fn(), activate: vi.fn(), ready: vi.fn(), companies: vi.fn(), machines: vi.fn() }));
vi.mock('../../../services/machineRequestAdminApi', async importOriginal => {
  const original = await importOriginal<typeof import('../../../services/machineRequestAdminApi')>();
  return { ...original, getMachineRequest: api.get, downloadMachineRequestDocument: api.download, updateMachineRequestStatus: api.update,
    captureMachineRequestPayment: api.capture, cancelMachineRequestPayment: api.cancel, decideAdditionalMachineRequest: api.decide, decideAdditionalDocumentsRequest: api.decideDocuments, attachMachineRequestBusinessEntities: api.attach,
    linkMachineRequestCustomer: api.linkCustomer, configureMachineRequestSubscription: api.subscription, activateMachineRequest: api.activate, markMachineRequestReady: api.ready };
});
vi.mock('../../../services/companyService', () => ({ getCompanies: api.companies }));
vi.mock('../../../services/machineService', () => ({ getMachines: api.machines }));

const summary: MachineRequestListItem = {
  requestId: 'request-real-1', createdAt: '2026-09-20T10:00:00Z', status: 'pending',
  firstName: 'Claire', lastName: 'Martin', company: 'Ateliers réels', email: 'claire@example.com', phone: '+33 6 12 34 56 78',
  machineName: 'Compresseur réel', manufacturer: 'Atlas Copco', model: 'GA90', documentCount: 1, totalPages: 550, preparationTotal: 140.4,
};
const detail: MachineRequestDetail = {
  requestId: summary.requestId, createdAt: summary.createdAt, status: 'pending',
  client: { firstName: 'Claire', lastName: 'Martin', company: 'Ateliers réels', email: 'claire@example.com', phone: '+33 6 12 34 56 78' },
  machine: { machineName: 'Compresseur réel', manufacturer: 'Atlas Copco', model: 'GA90', serialNumber: 'SN-42', description: 'Machine réelle' },
  documents: [{ documentId: 'opaque-document-id', originalName: 'manuel-reel.pdf', size: 2_000_000, pageCount: 550 }],
  pricing: { totalPages: 550, includedPages: 400, additionalPages: 150, basePreparationPrice: 99.9, additionalPagePrice: .27, preparationTotal: 140.4, monthlySubscriptionPrice: 29.9 },
  payment: null,
};
const linkedDetail: MachineRequestDetail = { ...detail, payment: {
  paymentRequestId: 'payment-real-1', status: 'authorized', amount: 170.3, currency: 'EUR', authorizationInsufficient: false,
} };
const provisioningDetail: MachineRequestDetail = { ...detail, status: 'treated', payment: {
  paymentRequestId: 'payment-real-1', status: 'captured', amount: 115.42, currency: 'EUR',
  authorizationInsufficient: false, initialAuthorizationAmount: 170.3,
  provisioningStage: 'amountFinalized', companyId: null, machineId: null,
} };

let root: Root | null = null;
let container: HTMLDivElement | null = null;
const token = vi.fn().mockResolvedValue('token');
const retry = vi.fn().mockResolvedValue(undefined);
const updated = vi.fn();

async function renderView(overrides: Partial<React.ComponentProps<typeof MachineRequestsView>> = {}) {
  container = document.createElement('div'); document.body.appendChild(container); root = createRoot(container);
  const props: React.ComponentProps<typeof MachineRequestsView> = { requests: [summary], isLoading: false, hasLoadingError: false, getAccessToken: token, onRetry: retry, onRequestUpdated: updated, ...overrides };
  await act(async () => root?.render(<MachineRequestsView {...props} />));
}

async function openDetail(data: MachineRequestDetail = detail) {
  api.get.mockResolvedValue({ kind: 'success', data });
  const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent?.includes('Voir la demande'))!;
  await act(async () => button.click());
}

beforeEach(() => {
  api.get.mockReset(); api.download.mockReset(); api.update.mockReset(); api.capture.mockReset(); api.cancel.mockReset(); api.decide.mockReset(); api.decideDocuments.mockReset(); api.attach.mockReset(); api.linkCustomer.mockReset(); api.subscription.mockReset(); api.activate.mockReset(); api.ready.mockReset();
  api.companies.mockReset().mockResolvedValue({ kind: 'success', data: [{ id: 'company-a', name: 'Atelier A', status: 'active' }, { id: 'company-b', name: 'Atelier B', status: 'active' }] });
  api.machines.mockReset().mockResolvedValue({ kind: 'success', data: [{ id: 'machine-a', companyId: 'company-a', name: 'Compresseur A', reference: 'A-1', status: 'active', hasAssistantConfigured: true, isAccessible: true }, { id: 'machine-b', companyId: 'company-b', name: 'Machine B', reference: null, status: 'active', hasAssistantConfigured: true, isAccessible: true }] });
  updated.mockClear(); retry.mockClear();
});
afterEach(async () => { if (root) await act(async () => root?.unmount()); container?.remove(); root = null; container = null; vi.restoreAllMocks(); });

describe('MachineRequestsView', () => {
  it('confirms and marks a captured machine ready, then hides the action', async () => {
    const eligible: MachineRequestDetail = { ...provisioningDetail, preparationStatus: 'pending' };
    const ready: MachineRequestDetail = { ...eligible, preparationStatus: 'ready', readyAtUtc: '2026-09-25T12:00:00Z' };
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    api.ready.mockResolvedValue({ kind: 'success', data: ready });
    await renderView(); await openDetail(eligible);
    const button = () => Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Marquer la machine comme prête');
    expect(button()).toBeTruthy();
    await act(async () => button()!.click());
    expect(api.ready).not.toHaveBeenCalled();
    await act(async () => button()!.click());
    expect(confirm).toHaveBeenCalledWith('Confirmer que cette machine est prête ?\nLe client recevra un email lui indiquant qu’elle est disponible dans DiagLink.');
    expect(api.ready).toHaveBeenCalledWith(token, eligible.requestId);
    expect(container?.textContent).toContain('Machine prête');
    expect(button()).toBeUndefined();
  });

  it('uses the documents-ready wording and restores ready state from backend detail', async () => {
    const eligible: MachineRequestDetail = { ...provisioningDetail, requestKind: 'additionalDocuments', preparationStatus: 'pending' };
    const ready: MachineRequestDetail = { ...eligible, preparationStatus: 'ready', readyAtUtc: '2026-09-25T12:00:00Z' };
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    api.ready.mockResolvedValue({ kind: 'success', data: ready });
    await renderView({ requests: [{ ...summary, requestKind: 'additionalDocuments' }] }); await openDetail(eligible);
    const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Marquer les documents comme intégrés')!;
    await act(async () => button.click());
    expect(container?.textContent).toContain('Documents intégrés');
    api.get.mockResolvedValue({ kind: 'success', data: ready });
    await act(async () => Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Retour aux demandes')!.click());
    await act(async () => Array.from(container!.querySelectorAll('button')).find(item => item.textContent?.includes('Voir la demande'))!.click());
    expect(container?.textContent).toContain('Documents intégrés');
    expect(container?.textContent).not.toContain('Marquer les documents comme intégrés');
  });

  it.each([
    ['authorized', 'pending'], ['cancelled', 'rejected'], ['abandoned', 'rejected'],
  ] as const)('does not expose Ready for %s payment', async (status, requestStatus) => {
    await renderView();
    await openDetail({ ...linkedDetail, status: requestStatus, payment: { ...linkedDetail.payment!, status } });
    expect(container?.textContent).not.toContain('Marquer la machine comme prête');
    expect(container?.textContent).not.toContain('Marquer les documents comme intégrés');
  });
  it('renders the real list returned by its API owner', async () => {
    await renderView();
    expect(container?.textContent).toContain('Ateliers réels');
    expect(container?.textContent).toContain('Compresseur réel');
    expect(container?.textContent).toContain('550');
  });

  it('renders empty, loading and error states without mock fallbacks', async () => {
    await renderView({ requests: [] }); expect(container?.textContent).toContain('Aucune nouvelle demande.');
    await act(async () => root?.render(<MachineRequestsView requests={[]} isLoading hasLoadingError={false} getAccessToken={token} onRetry={retry} onRequestUpdated={updated} />));
    expect(container?.textContent).toContain('Chargement des demandes');
    await act(async () => root?.render(<MachineRequestsView requests={[]} isLoading={false} hasLoadingError getAccessToken={token} onRetry={retry} onRequestUpdated={updated} />));
    expect(container?.textContent).toContain('Impossible de charger les nouvelles demandes.');
  });

  it('loads and displays the real detail and downloads through its opaque document id', async () => {
    api.download.mockResolvedValue({ kind: 'success', data: null });
    await renderView(); await openDetail();
    expect(api.get).toHaveBeenCalledWith(token, 'request-real-1');
    expect(container?.textContent).toContain('manuel-reel.pdf');
    expect(container?.textContent).toContain('Machine réelle');
    expect(container?.textContent).toContain('140,40');
    const download = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Télécharger')!;
    await act(async () => download.click());
    expect(api.download).toHaveBeenCalledWith(token, 'request-real-1', 'opaque-document-id', 'manuel-reel.pdf');
  });

  it('shows both actions for a pending request', async () => {
    await renderView(); await openDetail();
    expect(container?.textContent).toContain('Refuser la demande');
    expect(container?.textContent).toContain('Accepter la demande');
  });

  it('accepts an additional-machine request through its dedicated action without CAS A selectors', async () => {
    const additional = { ...linkedDetail, requestKind: 'additionalMachine' as const, companyId: 'company-a', requestedByUserId: 'admin-a' };
    const accepted: MachineRequestDetail = { ...additional, status: 'treated', payment: { ...additional.payment!, status: 'captured', amount: 119.17, initialAuthorizationAmount: 134.12, preparationAmount: 104.22, serviceAmountCents: 495, provisioningStage: 'businessEntitiesCreated', companyId: 'company-a', machineId: 'machine-new' } };
    api.decide.mockResolvedValue({ kind: 'success', data: accepted });
    await renderView({ requests: [{ ...summary, requestKind: 'additionalMachine' }] });
    await openDetail(additional);
    expect(container?.textContent).toContain('Administrateur entreprise · Ajout de machine');
    expect(container?.textContent).toContain('Accepter la demande');
    expect(container?.querySelector('select[aria-label="Entreprise"]')).toBeNull();
    const accept = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Accepter la demande')!;
    await act(async () => accept.click());
    expect(api.decide).toHaveBeenCalledWith(token, detail.requestId, 'accept');
    expect(api.capture).not.toHaveBeenCalled();
    expect(container?.textContent).toContain('Machine créée — provisioning à finaliser');
    expect(container?.textContent).toContain('BusinessEntitiesCreated');
    expect(container?.textContent).toContain('Service proratisé');
    expect(container?.textContent).not.toContain('Machine activée');
    expect(container?.textContent).not.toContain('Préparer la facturation');
  });

  it('rejects an additional-machine request through the dedicated server action', async () => {
    const additional = { ...linkedDetail, requestKind: 'additionalMachine' as const };
    api.decide.mockResolvedValue({ kind: 'success', data: { ...additional, status: 'rejected', payment: { ...additional.payment!, status: 'cancelled' } } });
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await renderView({ requests: [{ ...summary, requestKind: 'additionalMachine' }] }); await openDetail(additional);
    await act(async () => Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Refuser la demande')!.click());
    expect(api.decide).toHaveBeenCalledWith(token, detail.requestId, 'reject');
    expect(api.cancel).not.toHaveBeenCalled();
    expect(container?.textContent).toContain('Refusée');
  });

  it('renders and accepts AdditionalDocuments without exposing provisioning actions', async () => {
    const additionalDocuments: MachineRequestDetail = {
      ...linkedDetail,
      requestKind: 'additionalDocuments',
      companyId: 'company-a',
      requestedByUserId: 'admin-a',
      machine: { ...detail.machine, machineName: 'Compresseur existant' },
      pricing: { ...detail.pricing, totalPages: 370, additionalPages: 370, preparationTotal: 99.9 },
      payment: { ...linkedDetail.payment!, amount: 99.9 },
    };
    const accepted: MachineRequestDetail = {
      ...additionalDocuments,
      status: 'treated',
      payment: { ...additionalDocuments.payment!, status: 'captured' },
    };
    api.decideDocuments.mockResolvedValue({ kind: 'success', data: accepted });

    await renderView({ requests: [{ ...summary, requestKind: 'additionalDocuments', machineName: 'Compresseur existant', totalPages: 370, preparationTotal: 99.9 }] });
    expect(container?.textContent).toContain('Ajout de documents');
    await openDetail(additionalDocuments);

    expect(container?.textContent).toContain('Administrateur entreprise · Ajout de documents');
    expect(container?.textContent).toContain('Compresseur existant');
    expect(container?.textContent).toContain('370');
    expect(container?.textContent).toContain('0,27');
    expect(container?.textContent).toContain('99,90');
    expect(container?.textContent).not.toContain('Provisionnement');
    expect(container?.textContent).not.toContain('Configurer l’abonnement');
    expect(container?.textContent).not.toContain('Crédit inclus');

    const accept = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Accepter')!;
    await act(async () => accept.click());

    expect(api.decideDocuments).toHaveBeenCalledWith(token, detail.requestId, 'accept');
    expect(api.capture).not.toHaveBeenCalled();
    expect(api.update).not.toHaveBeenCalled();
    expect(container?.textContent).toContain('Documents reçus');
    expect(container?.textContent).not.toContain('Machine activée');
  });

  it('explains the one-page AdditionalDocuments minimum to the Super Admin', async () => {
    const onePage: MachineRequestDetail = {
      ...linkedDetail,
      requestKind: 'additionalDocuments',
      machine: { ...detail.machine, machineName: 'Compresseur existant' },
      pricing: { ...detail.pricing, totalPages: 1, additionalPages: 1, additionalPagePrice: 0.27, preparationTotal: 0.5 },
      payment: { ...linkedDetail.payment!, amount: 0.5 },
    };
    await renderView({ requests: [{ ...summary, requestKind: 'additionalDocuments', totalPages: 1, preparationTotal: 0.5 }] });
    await openDetail(onePage);

    expect(container?.textContent).toContain('Tarif documentaire');
    expect(container?.textContent).toContain('1 × 0,27');
    expect(container?.textContent).toContain('Minimum par demande');
    expect(container?.textContent).toContain('0,50');
  });

  it('rejects AdditionalDocuments through its dedicated action and shows the cancelled authorization', async () => {
    const additionalDocuments: MachineRequestDetail = {
      ...linkedDetail,
      requestKind: 'additionalDocuments',
      payment: { ...linkedDetail.payment!, amount: 99.9 },
    };
    api.decideDocuments.mockResolvedValue({
      kind: 'success',
      data: { ...additionalDocuments, status: 'rejected', payment: { ...additionalDocuments.payment!, status: 'cancelled' } },
    });
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await renderView({ requests: [{ ...summary, requestKind: 'additionalDocuments' }] });
    await openDetail(additionalDocuments);

    const reject = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Refuser')!;
    await act(async () => reject.click());

    expect(api.decideDocuments).toHaveBeenCalledWith(token, detail.requestId, 'reject');
    expect(api.cancel).not.toHaveBeenCalled();
    expect(container?.textContent).toContain('Autorisation annulée');
  });

  it('does not offer AdditionalDocuments decisions until the payment is authorized', async () => {
    const pending: MachineRequestDetail = {
      ...detail,
      requestKind: 'additionalDocuments',
      payment: { paymentRequestId: 'payment-documents', status: 'pending', amount: 99.9, currency: 'EUR', authorizationInsufficient: false },
    };
    await renderView({ requests: [{ ...summary, requestKind: 'additionalDocuments' }] });
    await openDetail(pending);
    expect(Array.from(container!.querySelectorAll('button')).some(item => item.textContent === 'Accepter')).toBe(false);
    expect(Array.from(container!.querySelectorAll('button')).some(item => item.textContent === 'Refuser')).toBe(false);
  });

  it('shows an abandoned AdditionalDocuments attempt as history without an admin action', async () => {
    const abandoned: MachineRequestDetail = {
      ...detail, status: 'rejected', requestKind: 'additionalDocuments',
      payment: { paymentRequestId: 'payment-abandoned', status: 'abandoned', amount: 0.27, currency: 'EUR', authorizationInsufficient: false },
    };
    await renderView({ requests: [{ ...summary, status: 'rejected', requestKind: 'additionalDocuments' }] });
    await openDetail(abandoned);

    expect(container?.textContent).toContain('Demande abandonnée avant autorisation');
    expect(container?.textContent).not.toContain('Autorisation annulée');
    expect(Array.from(container!.querySelectorAll('button')).some(item => item.textContent === 'Accepter' || item.textContent === 'Refuser')).toBe(false);
  });

  it('provisions an additional machine through CustomerLinked and stops at SubscriptionCreated', async () => {
    const business: MachineRequestDetail = { ...detail, status: 'treated', requestKind: 'additionalMachine', payment: {
      paymentRequestId: 'payment-additional', status: 'captured', amount: 119.17, currency: 'EUR',
      authorizationInsufficient: false, provisioningStage: 'businessEntitiesCreated', companyId: 'company-a', machineId: 'machine-new',
    } };
    const customerLinked: MachineRequestDetail = { ...business, payment: { ...business.payment!, provisioningStage: 'customerLinked' } };
    const subscriptionCreated: MachineRequestDetail = { ...business, payment: { ...business.payment!, provisioningStage: 'subscriptionCreated' } };
    api.linkCustomer.mockResolvedValue({ kind: 'success', data: customerLinked });
    api.subscription.mockResolvedValue({ kind: 'success', data: subscriptionCreated });
    await renderView({ requests: [{ ...summary, requestKind: 'additionalMachine' }] });
    await openDetail(business);

    const first = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Configurer l’abonnement')!;
    await act(async () => first.click());
    expect(api.linkCustomer).toHaveBeenCalledWith(token, detail.requestId);
    expect(container?.textContent).toContain('Customer Stripe existant vérifié');

    const second = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Configurer l’abonnement')!;
    await act(async () => second.click());
    expect(api.subscription).toHaveBeenCalledWith(token, detail.requestId);
    expect(container?.textContent).toContain('Abonnement mis à jour — activation à finaliser');
    expect(container?.textContent).not.toContain('Machine activée');
    const finalise = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Finaliser l’activation')!;
    const completed: MachineRequestDetail = { ...business, payment: { ...business.payment!, provisioningStage: 'completed' } };
    api.activate.mockResolvedValue({ kind: 'success', data: completed });
    await act(async () => finalise.click());
    expect(api.activate).toHaveBeenCalledWith(token, detail.requestId);
    expect(container?.textContent).toContain('Machine activée');
    expect(container?.textContent).toContain('10,00 € de crédit Agent disponibles');
    expect(container?.textContent).toContain('Documents reçus');
    expect(container?.textContent).not.toContain('Documents ingérés');
  });

  it('describes an AdditionalMachine customer verification failure without CAS A linking wording', async () => {
    const business: MachineRequestDetail = { ...detail, status: 'treated', requestKind: 'additionalMachine', payment: {
      paymentRequestId: 'payment-additional', status: 'captured', amount: 114.03, currency: 'EUR',
      authorizationInsufficient: false, provisioningStage: 'businessEntitiesCreated', companyId: 'company-a', machineId: 'machine-new',
    } };
    api.linkCustomer.mockResolvedValue({ kind: 'conflict', message: "Le PaymentMethod Stripe n'appartient pas au Customer attendu." });
    await renderView({ requests: [{ ...summary, requestKind: 'additionalMachine' }] });
    await openDetail(business);

    const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Configurer l’abonnement')!;
    await act(async () => button.click());

    expect(container?.textContent).toContain('Le Customer Stripe existant n’a pas pu être vérifié.');
    expect(container?.textContent).not.toContain('Le client Stripe n’a pas pu être rattaché.');
    expect(container?.textContent).toContain('Configurer l’abonnement');
  });

  it('shows the resumable initial-period checkpoint for an additional machine', async () => {
    const initial: MachineRequestDetail = { ...detail, status: 'treated', requestKind: 'additionalMachine', payment: {
      paymentRequestId: 'payment-additional', status: 'captured', amount: 119.17, currency: 'EUR',
      authorizationInsufficient: false, provisioningStage: 'initialPeriodCreated', companyId: 'company-a', machineId: 'machine-new',
    } };
    await renderView({ requests: [{ ...summary, requestKind: 'additionalMachine' }] }); await openDetail(initial);
    expect(container?.textContent).toContain('Période initiale créée — activation à finaliser');
    expect(container?.textContent).toContain('Finaliser l’activation');
    expect(container?.textContent).not.toContain('Machine activée');
  });

  it.each(['treated', 'rejected'] as const)('hides both actions for a %s request', async status => {
    await renderView(); await openDetail({ ...detail, status });
    expect(container?.textContent).not.toContain('Refuser la demande');
    expect(container?.textContent).not.toContain('Accepter la demande');
  });

  it('updates pending to treated and reports the server detail to the badge owner', async () => {
    const treated = { ...detail, status: 'treated' as const };
    api.update.mockResolvedValue({ kind: 'success', data: treated });
    await renderView(); await openDetail();
    const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent?.includes('Accepter la demande'))!;
    await act(async () => button.click());
    expect(api.update).toHaveBeenCalledWith(token, detail.requestId, 'treated');
    expect(updated).toHaveBeenCalledWith(treated);
    expect(container?.textContent).toContain('Traitée');
    expect(container?.textContent).not.toContain('Refuser la demande');
    expect(container?.textContent).not.toContain('Accepter la demande');
  });

  it('captures a linked payment before accepting the request', async () => {
    const treated = { ...linkedDetail, status: 'treated' as const, payment: { ...linkedDetail.payment!, status: 'captured' as const, amount: 115.42, initialAuthorizationAmount: 170.3 } };
    api.capture.mockResolvedValue({ kind: 'success', data: { paymentRequestId: 'payment-real-1', status: 'captured', amount: 115.42, currency: 'EUR', initialAuthorizationAmount: 170.3 } });
    api.update.mockResolvedValue({ kind: 'success', data: treated });
    await renderView(); await openDetail(linkedDetail);
    expect(container?.textContent).toContain('170,30');
    expect(container?.textContent).toContain('autorisés');
    expect(container?.textContent).toContain('non encaissés');

    const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent?.includes('Accepter la demande'))!;
    await act(async () => button.click());
    expect(api.capture).toHaveBeenCalledWith(token, 'payment-real-1');
    expect(api.update).toHaveBeenCalledWith(token, linkedDetail.requestId, 'treated');
    expect(api.update.mock.invocationCallOrder[0]).toBeGreaterThan(api.capture.mock.invocationCallOrder[0]);
    expect(container?.textContent).toContain('115,42');
    expect(container?.textContent).toContain('Autorisation initiale');
    expect(container?.textContent).toContain('170,30');
  });

  it('does not treat a request when Stripe capture fails', async () => {
    api.capture.mockResolvedValue({ kind: 'error' });
    await renderView(); await openDetail(linkedDetail);
    const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent?.includes('Accepter la demande'))!;
    await act(async () => button.click());

    expect(api.update).not.toHaveBeenCalled();
    expect(container?.textContent).toContain('encaissement Stripe n’a pas pu être confirmé');
    expect(container?.textContent).toContain('En attente');
  });

  it('filters machines by company and freezes the selected business entities', async () => {
    const attached: MachineRequestDetail = { ...provisioningDetail, payment: {
      ...provisioningDetail.payment!, provisioningStage: 'businessEntitiesCreated',
      companyId: 'company-a', machineId: 'machine-a',
    } };
    api.attach.mockResolvedValue({ kind: 'success', data: attached });
    await renderView(); await openDetail(provisioningDetail);

    expect(container?.textContent).toContain('Provisionnement');
    expect(container?.textContent).toContain('115,42');
    const company = container!.querySelector('select[aria-label="Entreprise"]') as HTMLSelectElement;
    const machine = container!.querySelector('select[aria-label="Machine"]') as HTMLSelectElement;
    const confirm = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Confirmer le rattachement') as HTMLButtonElement;
    expect(confirm.disabled).toBe(true);
    await act(async () => { company.value = 'company-a'; company.dispatchEvent(new Event('change', { bubbles: true })); });
    expect(machine.textContent).toContain('Compresseur A');
    expect(machine.textContent).not.toContain('Machine B');
    await act(async () => { machine.value = 'machine-a'; machine.dispatchEvent(new Event('change', { bubbles: true })); });
    expect(confirm.disabled).toBe(false);
    await act(async () => confirm.click());

    expect(api.attach).toHaveBeenCalledWith(token, provisioningDetail.requestId, 'company-a', 'machine-a');
    expect(container?.textContent).toContain('Atelier A');
    expect(container?.textContent).toContain('Compresseur A');
    expect(container?.textContent).toContain('Rattachement effectué');
    expect(container?.textContent).not.toContain('Confirmer le rattachement');
  });

  it('prepares billing and immediately displays the linked Stripe customer checkpoint', async () => {
    const attached: MachineRequestDetail = { ...provisioningDetail, payment: { ...provisioningDetail.payment!,
      provisioningStage: 'businessEntitiesCreated', companyId: 'company-a', machineId: 'machine-a' } };
    const linked: MachineRequestDetail = { ...attached, payment: { ...attached.payment!, provisioningStage: 'customerLinked' } };
    api.linkCustomer.mockResolvedValue({ kind: 'success', data: linked });
    await renderView(); await openDetail(attached);
    expect(container?.textContent).toContain('À préparer');
    const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Préparer la facturation')!;
    await act(async () => button.click());
    expect(api.linkCustomer).toHaveBeenCalledWith(token, attached.requestId);
    expect(container?.textContent).toContain('Client Stripe rattaché');
    expect(container?.textContent).not.toContain('Préparer la facturation');
  });

  it('configures the subscription only from CustomerLinked and hides the action after success', async () => {
    const customerLinked: MachineRequestDetail = { ...provisioningDetail, payment: { ...provisioningDetail.payment!,
      provisioningStage: 'customerLinked', companyId: 'company-a', machineId: 'machine-a' } };
    const configured: MachineRequestDetail = { ...customerLinked, payment: { ...customerLinked.payment!, provisioningStage: 'subscriptionCreated' } };
    api.subscription.mockResolvedValue({ kind: 'success', data: configured });
    await renderView(); await openDetail(customerLinked);
    expect(container?.textContent).toContain('À configurer');
    const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Configurer l’abonnement')!;
    await act(async () => button.click());
    expect(api.subscription).toHaveBeenCalledWith(token, customerLinked.requestId);
    expect(container?.textContent).toContain('Abonnement configuré');
    expect(container?.textContent).not.toContain('Configurer l’abonnement');
  });

  it('keeps CustomerLinked and exposes a retry when subscription configuration fails', async () => {
    const customerLinked: MachineRequestDetail = { ...provisioningDetail, payment: { ...provisioningDetail.payment!,
      provisioningStage: 'customerLinked', companyId: 'company-a', machineId: 'machine-a' } };
    api.subscription.mockResolvedValue({ kind: 'error' });
    await renderView(); await openDetail(customerLinked);
    const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Configurer l’abonnement')!;
    await act(async () => button.click());
    expect(container?.textContent).toContain('abonnement n’a pas pu être configuré');
    expect(container?.textContent).toContain('Configurer l’abonnement');
  });

  it('activates a configured machine and displays included credit without wallet wording', async () => {
    const configured: MachineRequestDetail = { ...provisioningDetail, payment: { ...provisioningDetail.payment!,
      provisioningStage: 'subscriptionCreated', companyId: 'company-a', machineId: 'machine-a' } };
    const completed: MachineRequestDetail = { ...configured, payment: { ...configured.payment!, provisioningStage: 'completed' } };
    api.activate.mockResolvedValue({ kind: 'success', data: completed });
    await renderView(); await openDetail(configured);
    const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent === 'Activer la machine')!;
    await act(async () => button.click());
    expect(api.activate).toHaveBeenCalledWith(token, configured.requestId);
    expect(container?.textContent).toContain('Machine activée');
    expect(container?.textContent).toContain('10,00 € de crédit inclus');
    expect(container?.textContent).not.toContain('Wallet');
    expect(container?.textContent).not.toContain('Activer la machine');
  });

  it('requires confirmation before changing pending to rejected', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    api.update.mockResolvedValue({ kind: 'success', data: { ...detail, status: 'rejected' } });
    await renderView(); await openDetail();
    const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent?.includes('Refuser'))!;
    await act(async () => button.click()); expect(api.update).not.toHaveBeenCalled();
    await act(async () => button.click());
    expect(confirm).toHaveBeenCalledWith('Confirmer le refus de cette demande ?');
    expect(api.update).toHaveBeenCalledWith(token, detail.requestId, 'rejected');
    expect(container?.textContent).toContain('Refusée');
    expect(container?.textContent).not.toContain('Refuser la demande');
    expect(container?.textContent).not.toContain('Accepter la demande');
  });

  it('cancels a linked authorization before rejecting the request', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    api.cancel.mockResolvedValue({ kind: 'success', data: { paymentRequestId: 'payment-real-1', status: 'cancelled', amount: 140.4, currency: 'EUR' } });
    api.update.mockResolvedValue({ kind: 'success', data: { ...linkedDetail, status: 'rejected', payment: { ...linkedDetail.payment!, status: 'cancelled' } } });
    await renderView(); await openDetail(linkedDetail);
    const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent?.includes('Refuser'))!;
    await act(async () => button.click());

    expect(api.cancel).toHaveBeenCalledWith(token, 'payment-real-1');
    expect(api.update.mock.invocationCallOrder[0]).toBeGreaterThan(api.cancel.mock.invocationCallOrder[0]);
  });

  it('does not reject a request when Stripe cancellation fails', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    api.cancel.mockResolvedValue({ kind: 'error' });
    await renderView(); await openDetail(linkedDetail);
    const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent?.includes('Refuser'))!;
    await act(async () => button.click());

    expect(api.update).not.toHaveBeenCalled();
    expect(container?.textContent).toContain('annulation Stripe n’a pas pu être confirmée');
    expect(container?.textContent).toContain('En attente');
  });

  it('keeps the pending state when a status update fails', async () => {
    api.update.mockResolvedValue({ kind: 'error' });
    await renderView(); await openDetail();
    const button = Array.from(container!.querySelectorAll('button')).find(item => item.textContent?.includes('Accepter la demande'))!;
    await act(async () => button.click());
    expect(container?.textContent).toContain('En attente');
    expect(container?.textContent).toContain('Le statut de la demande n’a pas pu être modifié.');
    expect(updated).not.toHaveBeenCalled();
  });

  it('removes the pending navigation badge immediately after a successful status update', async () => {
    api.update.mockResolvedValue({ kind: 'success', data: { ...detail, status: 'treated' } });
    container = document.createElement('div'); document.body.appendChild(container); root = createRoot(container);
    function Harness() {
      const [requests, setRequests] = useState([summary]);
      return <><Navigation role="diaglink_super_admin" currentView="machine-requests" onSelectView={() => undefined} pendingMachineRequestCount={requests.filter(item => item.status === 'pending').length} /><MachineRequestsView requests={requests} isLoading={false} hasLoadingError={false} getAccessToken={token} onRetry={retry} onRequestUpdated={request => setRequests(items => items.map(item => item.requestId === request.requestId ? { ...item, status: request.status } : item))} /></>;
    }
    await act(async () => root?.render(<Harness />));
    expect(Array.from(container.querySelectorAll('button')).find(item => item.textContent?.includes('Nouvelles demandes'))?.textContent).toContain('1');
    api.get.mockResolvedValue({ kind: 'success', data: detail });
    await act(async () => Array.from(container!.querySelectorAll('button')).find(item => item.textContent?.includes('Voir la demande'))?.click());
    await act(async () => Array.from(container!.querySelectorAll('button')).find(item => item.textContent?.includes('Accepter la demande'))?.click());
    expect(Array.from(container.querySelectorAll('button')).find(item => item.textContent?.includes('Nouvelles demandes'))?.textContent).not.toContain('1');
  });
});
