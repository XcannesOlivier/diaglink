import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { checkoutReturnedMessage } from '../../../pages/start/checkoutPopup';
import { AdditionalDocumentsRequestDialog } from '../AdditionalDocumentsRequestDialog';

const api = vi.hoisted(() => ({ stage: vi.fn(), start: vi.fn(), get: vi.fn(), cancel: vi.fn(), inspect: vi.fn() }));
vi.mock('../../../services/additionalDocumentsRequestApi', () => ({
  stageAdditionalDocuments: api.stage, startAdditionalDocumentsPayment: api.start, getAdditionalDocumentsPayment: api.get,
  cancelAdditionalDocumentsRequest: api.cancel,
}));
vi.mock('../../../pages/start/pdfSelection', () => ({
  formatFileSize: () => '1 Ko', inspectPdfFiles: api.inspect,
}));

const machine = { id: 'm1', companyId: 'c1', name: 'Compresseur Alpha', reference: null, status: 'active', hasAssistantConfigured: true, isAccessible: true };
const staged = { requestId: 'r1', paymentRequestId: 'r1', status: 'pending', targetMachineId: 'm1', documentCount: 1, totalPages: 100, amountCents: 2700, currency: 'EUR' };

async function renderDialog() {
  const host = document.createElement('div'); document.body.appendChild(host); const root = createRoot(host);
  await act(async () => root.render(<AdditionalDocumentsRequestDialog open onOpenChange={vi.fn()} machine={machine} getAccessToken={async () => 'token'} />));
  return { host, root };
}
async function selectPdf(pageCount = 100) {
  const file = new File(['pdf'], 'manual.pdf', { type: 'application/pdf' });
  api.inspect.mockResolvedValue({ accepted: [{ id: 1, file, pageCount }], errors: [] });
  const input = document.body.querySelector('input[type="file"]') as HTMLInputElement;
  await act(async () => Object.defineProperty(input, 'files', { configurable: true, value: [file] }));
  await act(async () => input.dispatchEvent(new Event('change', { bubbles: true })));
}
const click = async (label: string) => {
  const button = [...document.body.querySelectorAll('button')].find(item => item.textContent?.includes(label));
  await act(async () => button?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
};

describe('AdditionalDocumentsRequestDialog', () => {
  let popup: { location: { href: string }; closed: boolean; close: ReturnType<typeof vi.fn>; postMessage: ReturnType<typeof vi.fn> };
  beforeEach(() => {
    document.body.innerHTML = ''; sessionStorage.clear();
    vi.restoreAllMocks(); api.stage.mockReset(); api.start.mockReset(); api.get.mockReset(); api.cancel.mockReset(); api.inspect.mockReset();
    popup = { location: { href: '' }, closed: false, close: vi.fn(), postMessage: vi.fn() };
    vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window); vi.spyOn(window, 'focus').mockImplementation(() => undefined);
    vi.spyOn(crypto, 'randomUUID').mockReturnValue('11111111-1111-4111-8111-111111111111');
  });

  it('shows the existing machine, per-page estimate and no subscription pricing', async () => {
    await renderDialog(); await selectPdf();
    expect(document.body.textContent).toContain('Compresseur Alpha'); expect(document.body.textContent).toContain('100 pages');
    expect(document.body.textContent).toContain('Demander l’ajout de documents');
    expect(document.body.textContent).toContain('0,27 € HT / page'); expect(document.body.textContent).toContain('27,00');
    expect(document.body.textContent).not.toContain('29,90'); expect(document.body.textContent).not.toContain('99,90');
    expect(document.body.textContent).not.toContain('10,00'); expect(document.body.textContent).not.toContain('Fabricant');
  });

  it.each([
    [1, '0,50', true],
    [2, '0,54', false],
    [3, '0,81', false],
  ])('shows the indicative total for %i page(s)', async (pages, total, minimumApplies) => {
    const rendered = await renderDialog(); await selectPdf(pages);
    expect(document.body.textContent).toContain(`${pages} × 0,27`);
    expect(document.body.textContent).toContain(total);
    expect(document.body.textContent?.includes('Minimum par demande')).toBe(minimumApplies);
    await act(async () => rendered.root.unmount()); rendered.host.remove();
  });

  it('uses the authoritative staged minimum for a one-page checkout', async () => {
    api.stage.mockResolvedValue({ ...staged, totalPages: 1, amountCents: 50 });
    api.start.mockResolvedValue({ status: 'pending', checkoutUrl: 'https://checkout.stripe.test/docs' });
    await renderDialog(); await selectPdf(1); await click('Continuer');
    expect(api.stage).toHaveBeenCalledTimes(1);
    expect(api.start).toHaveBeenCalledWith(expect.any(Function), 'r1');
    expect(document.body.textContent).toContain('0,50');
    expect(popup.location.href).toBe('https://checkout.stripe.test/docs');
  });

  it('stages once, uses server values, opens named popup and does not poll before trusted return', async () => {
    api.stage.mockResolvedValue(staged); api.start.mockResolvedValue({ status: 'pending', checkoutUrl: 'https://checkout.stripe.test/docs' });
    api.get.mockResolvedValue({ status: 'authorized', amount: 27, currency: 'EUR' });
    await renderDialog(); await selectPdf(); await click('Continuer');
    expect(api.stage).toHaveBeenCalledTimes(1); expect(api.stage).toHaveBeenCalledWith(expect.any(Function), 'm1', expect.any(Array), '11111111-1111-4111-8111-111111111111');
    expect(window.open).toHaveBeenCalledWith('', 'diaglink-machine-request-checkout');
    expect(popup.location.href).toBe('https://checkout.stripe.test/docs'); expect(api.get).not.toHaveBeenCalled();

    await act(async () => window.dispatchEvent(new MessageEvent('message', { origin: 'https://evil.test', source: popup as unknown as Window, data: { type: checkoutReturnedMessage } })));
    await act(async () => window.dispatchEvent(new MessageEvent('message', { origin: window.location.origin, source: window, data: { type: checkoutReturnedMessage } })));
    expect(api.get).not.toHaveBeenCalled();
    await act(async () => window.dispatchEvent(new MessageEvent('message', { origin: window.location.origin, source: popup as unknown as Window, data: { type: checkoutReturnedMessage } })));
    expect(api.get).toHaveBeenCalledTimes(1); expect(document.body.textContent).toContain('Demande envoy');
    expect(document.body.textContent).toContain('100 page'); expect(document.body.textContent).toContain('27,00'); expect(document.body.textContent).toContain('encaiss');
    expect(popup.close).toHaveBeenCalled();
  });

  it('keeps requestId and PDFs on payment retry without generating a new UUID', async () => {
    api.stage.mockResolvedValue(staged); api.start.mockRejectedValueOnce(new Error('Checkout indisponible')).mockResolvedValueOnce({ status: 'pending', checkoutUrl: 'https://checkout.stripe.test/retry' });
    await renderDialog(); await selectPdf(); await click('Continuer');
    expect(document.body.textContent).toContain('Rouvrir le paiement'); await click('Rouvrir');
    expect(api.stage).toHaveBeenCalledTimes(1); expect(api.start).toHaveBeenCalledTimes(2);
    expect(api.start).toHaveBeenNthCalledWith(2, expect.any(Function), 'r1'); expect(crypto.randomUUID).toHaveBeenCalledTimes(1);
  });

  it('closes the blank checkout popup when the payment POST fails', async () => {
    api.stage.mockResolvedValue({ ...staged, totalPages: 3, amountCents: 81 });
    api.start.mockRejectedValue(new Error('Montant refusé'));
    api.inspect.mockResolvedValue({
      accepted: [{ id: 1, file: new File(['pdf'], 'three-pages.pdf', { type: 'application/pdf' }), pageCount: 3 }],
      errors: [],
    });
    await renderDialog();
    const input = document.body.querySelector('input[type="file"]') as HTMLInputElement;
    await act(async () => Object.defineProperty(input, 'files', { configurable: true, value: [new File(['pdf'], 'three-pages.pdf', { type: 'application/pdf' })] }));
    await act(async () => input.dispatchEvent(new Event('change', { bubbles: true })));
    await click('Continuer');

    expect(api.start).toHaveBeenCalledWith(expect.any(Function), 'r1');
    expect(popup.location.href).toBe('');
    expect(popup.close).toHaveBeenCalledTimes(1);
    expect(document.body.textContent).toContain('Rouvrir le paiement');
  });

  it('keeps pending as recoverable and manual verification only calls GET', async () => {
    vi.useFakeTimers(); api.stage.mockResolvedValue(staged); api.start.mockResolvedValue({ status: 'pending', checkoutUrl: 'https://checkout.stripe.test/docs' });
    api.get.mockResolvedValue({ status: 'pending', amount: 27, currency: 'EUR' });
    await renderDialog(); await selectPdf(); await click('Continuer');
    await act(async () => window.dispatchEvent(new MessageEvent('message', { origin: window.location.origin, source: popup as unknown as Window, data: { type: checkoutReturnedMessage } })));
    await act(async () => vi.advanceTimersByTimeAsync(7500));
    expect(api.get).toHaveBeenCalledTimes(6); expect(document.body.textContent).toContain('Autorisation en cours');
    await click('Vérifier'); expect(api.get).toHaveBeenCalledTimes(7); expect(api.stage).toHaveBeenCalledTimes(1); expect(api.start).toHaveBeenCalledTimes(1);
    vi.useRealTimers();
  });

  it('detects a manually closed popup and verifies with GET only', async () => {
    vi.useFakeTimers();
    api.stage.mockResolvedValue(staged);
    api.start.mockResolvedValue({ status: 'pending', checkoutUrl: 'https://checkout.stripe.test/docs' });
    api.get.mockResolvedValueOnce({ status: 'pending', amount: 27, currency: 'EUR' })
      .mockResolvedValueOnce({ status: 'authorized', amount: 27, currency: 'EUR' });
    await renderDialog(); await selectPdf(); await click('Continuer');
    popup.closed = true;
    await act(async () => vi.advanceTimersByTimeAsync(500));
    expect(document.body.textContent).toContain('Autorisation en cours de vérification');
    expect(document.body.textContent).toContain('Vérifier l’autorisation');
    expect(api.stage).toHaveBeenCalledTimes(1); expect(api.start).toHaveBeenCalledTimes(1); expect(api.get).not.toHaveBeenCalled();
    await click('Vérifier');
    expect(api.get).toHaveBeenCalledTimes(1); expect(api.stage).toHaveBeenCalledTimes(1); expect(api.start).toHaveBeenCalledTimes(1);
    await click('Vérifier');
    expect(api.get).toHaveBeenCalledTimes(2); expect(document.body.textContent).toContain('Demande envoyée');
    vi.useRealTimers();
  });

  it('persists only the scoped request identity and resumes it after remount', async () => {
    api.stage.mockResolvedValue(staged);
    api.start.mockResolvedValue({ status: 'pending', checkoutUrl: 'https://checkout.stripe.test/docs' });
    const first = await renderDialog(); await selectPdf(); await click('Continuer');
    const key = 'diaglink:additional-documents:m1';
    expect(JSON.parse(sessionStorage.getItem(key)!)).toEqual({ requestId: 'r1', machineId: 'm1' });
    expect(sessionStorage.getItem(key)).not.toContain('manual.pdf');
    await act(async () => first.root.unmount()); first.host.remove();

    api.get.mockResolvedValue({ status: 'authorized', amount: 27, currency: 'EUR' });
    await renderDialog();
    expect(api.stage).toHaveBeenCalledTimes(1); expect(api.start).toHaveBeenCalledTimes(1);
    expect(api.get).toHaveBeenCalledWith(expect.any(Function), 'r1');
    expect(document.body.textContent).toContain('Demande envoyée');
    expect(sessionStorage.getItem(key)).toBeNull();
  });

  it('abandons a recovered pending request and immediately restores a blank selectable form', async () => {
    const key = 'diaglink:additional-documents:m1';
    sessionStorage.setItem(key, JSON.stringify({ requestId: 'r1', machineId: 'm1' }));
    api.get.mockResolvedValue({ status: 'pending', amount: 0.5, currency: 'EUR' });
    api.cancel.mockResolvedValue({ paymentRequestId: 'r1', paymentStatus: 'abandoned', requestStatus: 'rejected' });
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    await renderDialog();

    expect(document.body.textContent).toContain('Annuler la demande');
    await click('Annuler la demande');

    expect(confirm).toHaveBeenCalledWith('Annuler cette demande ?\nVous pourrez sélectionner de nouveaux documents ensuite.');
    expect(api.cancel).toHaveBeenCalledWith(expect.any(Function), 'r1');
    expect(sessionStorage.getItem(key)).toBeNull();
    expect(document.body.textContent).not.toContain('Rouvrir le paiement');
    expect(document.body.textContent).toContain('Continuer vers le paiement');
    await selectPdf(2);
    expect(document.body.textContent).toContain('manual.pdf');
  });

  it('keeps Close distinct from durable cancellation', async () => {
    sessionStorage.setItem('diaglink:additional-documents:m1', JSON.stringify({ requestId: 'r1', machineId: 'm1' }));
    api.get.mockResolvedValue({ status: 'pending', amount: 0.5, currency: 'EUR' });
    await renderDialog(); await click('Fermer');
    expect(api.cancel).not.toHaveBeenCalled();
  });

  it('does not offer cancellation after the recovered request was captured', async () => {
    sessionStorage.setItem('diaglink:additional-documents:m1', JSON.stringify({ requestId: 'r1', machineId: 'm1' }));
    api.get.mockResolvedValue({ status: 'captured', amount: 0.5, currency: 'EUR' });
    await renderDialog();
    expect(document.body.textContent).toContain('Demande envoyée');
    expect(document.body.textContent).not.toContain('Annuler la demande');
    expect(api.cancel).not.toHaveBeenCalled();
  });

  it('ignores a persisted attempt scoped to another machine', async () => {
    sessionStorage.setItem('diaglink:additional-documents:other', JSON.stringify({ requestId: 'other-request', machineId: 'other' }));
    await renderDialog();
    expect(document.body.textContent).not.toContain('Vérifier l’autorisation');
    expect(api.get).not.toHaveBeenCalled(); expect(api.stage).not.toHaveBeenCalled(); expect(api.start).not.toHaveBeenCalled();
  });
});
