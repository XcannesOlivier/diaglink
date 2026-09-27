import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MachineRequestSubmissionError } from '../../services/machineRequestApi';
import { MachineRequestPaymentTerminalError } from '../../services/machineRequestPaymentApi';
import { StartPage } from './StartPage';

const mocks = vi.hoisted(() => ({
  submitMachineRequest: vi.fn(),
  createPayment: vi.fn(),
  waitForAuthorization: vi.fn(),
  loadPdf: vi.fn().mockResolvedValue({ getPageCount: () => 12 }),
}));

vi.mock('pdf-lib', () => ({ PDFDocument: { load: mocks.loadPdf } }));
vi.mock('../../services/machineRequestApi', async importOriginal => {
  const original = await importOriginal<typeof import('../../services/machineRequestApi')>();
  return { ...original, submitMachineRequest: mocks.submitMachineRequest };
});
vi.mock('../../services/machineRequestPaymentApi', async importOriginal => {
  const original = await importOriginal<typeof import('../../services/machineRequestPaymentApi')>();
  return {
    ...original,
    createMachineRequestPayment: mocks.createPayment,
    waitForMachineRequestAuthorization: mocks.waitForAuthorization,
  };
});
vi.mock('../../components/marketing/PublicHeader', () => ({ PublicHeader: () => <header>DiagLink</header> }));
vi.mock('../../components/marketing/ClosingSections', () => ({ PublicFooter: () => <footer>Footer</footer> }));

let host: HTMLDivElement;
let root: Root;
let originalWindowName: string;
let originalOpener: Window | null;
let checkoutPopup: { location: { href: string }; close: ReturnType<typeof vi.fn>; postMessage: ReturnType<typeof vi.fn> };

async function renderPage() {
  host = document.createElement('div');
  document.body.appendChild(host);
  root = createRoot(host);
  await act(async () => root.render(<MemoryRouter><StartPage isAuthenticated={false} /></MemoryRouter>));
}

function setValue(name: string, value: string) {
  const input = host.querySelector(`[name="${name}"]`) as HTMLInputElement;
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

async function completeForm() {
  await act(async () => {
    setValue('firstName', 'Claire');
    setValue('lastName', 'Martin');
    setValue('company', 'Ateliers Martin');
    setValue('email', 'claire@example.com');
    setValue('phone', '+33 6 12 34 56 78');
    setValue('machineName', 'Compresseur');
    setValue('manufacturer', 'Atlas Copco');
    setValue('model', 'GA90');
  });

  const file = new File(['local-pdf'], 'manual.pdf', { type: 'application/pdf' });
  Object.defineProperty(file, 'arrayBuffer', { value: async () => new ArrayBuffer(8) });
  const input = host.querySelector('input[type="file"]') as HTMLInputElement;
  Object.defineProperty(input, 'files', { configurable: true, value: [file] });
  await act(async () => input.dispatchEvent(new Event('change', { bubbles: true })));
}

async function submitTwice() {
  const form = host.querySelector('form')!;
  await act(async () => {
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  });
}

beforeEach(() => {
  localStorage.clear();
  originalWindowName = window.name;
  originalOpener = window.opener;
  window.name = '';
  Object.defineProperty(window, 'opener', { configurable: true, value: null });
  mocks.submitMachineRequest.mockReset();
  mocks.createPayment.mockReset().mockResolvedValue({
    paymentRequestId: 'payment-42', status: 'pending', amount: 99.9, currency: 'EUR', checkoutUrl: 'https://checkout.stripe.test/session',
  });
  mocks.waitForAuthorization.mockReset().mockResolvedValue({ paymentRequestId: 'payment-42', status: 'authorized', amount: 99.9, currency: 'EUR' });
  mocks.loadPdf.mockClear();
  checkoutPopup = { location: { href: '' }, close: vi.fn(), postMessage: vi.fn() };
  vi.spyOn(window, 'open').mockReturnValue(checkoutPopup as unknown as Window);
  vi.spyOn(window, 'focus').mockImplementation(() => undefined);
  vi.spyOn(crypto, 'randomUUID').mockReturnValue('11111111-1111-4111-8111-111111111111');
});

afterEach(async () => {
  if (root) await act(async () => root.unmount());
  host?.remove();
  vi.restoreAllMocks();
  localStorage.clear();
  window.name = originalWindowName;
  Object.defineProperty(window, 'opener', { configurable: true, value: originalOpener });
});

describe('/commencer submission', () => {
  it('keeps the submit button disabled with a document-first label until the form is complete', async () => {
    await renderPage();

    const button = host.querySelector<HTMLButtonElement>('button[type="submit"]');
    expect(button).not.toBeNull();
    expect(button?.disabled).toBe(true);
    expect(button?.textContent).toBe('Ajouter vos documents pour continuer');
  });

  it('marks the machine description as optional', async () => {
    await renderPage();

    const description = host.querySelector<HTMLTextAreaElement>('textarea[name="description"]');
    expect(description?.closest('label')?.textContent).toContain('Description / informations complémentaires (facultatif)');
  });

  it('renders a dedicated Checkout return screen and not the empty form', async () => {
    const opener = { postMessage: vi.fn() } as unknown as Window;
    window.name = 'diaglink-machine-request-checkout';
    Object.defineProperty(window, 'opener', { configurable: true, value: opener });

    await renderPage();

    expect(host.textContent).toContain('Vérification de votre autorisation en cours…');
    expect(host.querySelector('form')).toBeNull();
    expect(opener.postMessage).toHaveBeenCalledWith(
      { type: 'diaglink:machine-request-checkout-returned' }, window.location.origin);
  });

  it('handles a Checkout return without opener without showing an empty form', async () => {
    window.name = 'diaglink-machine-request-checkout';

    await renderPage();

    expect(host.querySelector('form')).toBeNull();
    expect(host.textContent).toContain('Revenez à l’onglet d’origine');
  });

  it('accepts completion messages only from the same-origin opener', async () => {
    const opener = { postMessage: vi.fn() } as unknown as Window;
    window.name = 'diaglink-machine-request-checkout';
    Object.defineProperty(window, 'opener', { configurable: true, value: opener });
    const close = vi.spyOn(window, 'close').mockImplementation(() => undefined);
    await renderPage();

    await act(async () => window.dispatchEvent(new MessageEvent('message', {
      data: { type: 'diaglink:machine-request-submitted' }, origin: 'https://attacker.example', source: opener,
    })));
    await act(async () => window.dispatchEvent(new MessageEvent('message', {
      data: { type: 'diaglink:machine-request-submitted' }, origin: window.location.origin, source: window,
    })));
    expect(close).not.toHaveBeenCalled();

    await act(async () => window.dispatchEvent(new MessageEvent('message', {
      data: { type: 'diaglink:machine-request-submitted' }, origin: window.location.origin, source: opener,
    })));
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('keeps the normal form when /commencer is opened directly', async () => {
    await renderPage();
    expect(host.querySelector('form')).not.toBeNull();
    expect(host.textContent).not.toContain('Retour de Stripe');
  });

  it('shows preparation plus the maximum first subscription in the authorization total', async () => {
    await renderPage();
    await completeForm();

    expect(host.textContent).toContain('1er abonnement DiagLink');
    expect(host.textContent).toContain('Jusqu’à 29,90 € HT');
    expect(host.textContent).toContain('Autorisation maximale');
    expect(host.textContent?.replaceAll('\u00a0', ' ')).toContain('Autoriser 129,80 € et transmettre ma demande');
    expect(host.textContent).toContain('seul le montant réellement dû sera encaissé');
  });

  it('does not upload documents before the payment becomes authorized', async () => {
    let authorize!: () => void;
    mocks.waitForAuthorization.mockImplementation(() => new Promise<void>(resolve => { authorize = resolve; }));
    mocks.submitMachineRequest.mockResolvedValue({
      requestId: 'server-request-42', status: 'pending', createdAt: '2026-09-22T10:00:00Z',
      documentCount: 1, totalPages: 12, additionalPages: 0, preparationTotal: 99.9, authorizationInsufficient: false,
    });
    await renderPage();
    await completeForm();

    await submitTwice();
    expect(mocks.submitMachineRequest).not.toHaveBeenCalled();
    expect(host.textContent).toContain('Autorisation du paiement en attente');
    expect(window.open).toHaveBeenCalledWith('', 'diaglink-machine-request-checkout');

    await act(async () => window.dispatchEvent(new MessageEvent('message', {
      data: { type: 'diaglink:machine-request-checkout-returned' },
      origin: 'https://attacker.example',
      source: checkoutPopup as unknown as Window,
    })));
    expect(host.textContent).toContain('Autorisation du paiement en attente');

    await act(async () => window.dispatchEvent(new MessageEvent('message', {
      data: { type: 'diaglink:machine-request-checkout-returned' },
      origin: window.location.origin,
      source: checkoutPopup as unknown as Window,
    })));
    expect(mocks.submitMachineRequest).not.toHaveBeenCalled();

    await act(async () => authorize());
    expect(mocks.submitMachineRequest).toHaveBeenCalledTimes(1);
  });

  it('locks submission, prevents a duplicate, then renders only server-confirmed totals', async () => {
    let resolveRequest!: (value: unknown) => void;
    mocks.submitMachineRequest.mockImplementation(() => new Promise(resolve => { resolveRequest = resolve; }));
    await renderPage();
    await completeForm();

    await submitTwice();
    expect(mocks.submitMachineRequest).toHaveBeenCalledTimes(1);
    expect(host.textContent).toContain('Transmission de vos documents');
    expect(mocks.createPayment).toHaveBeenCalledWith(
      12, 'claire@example.com', '11111111-1111-4111-8111-111111111111');
    expect(mocks.waitForAuthorization).toHaveBeenCalledWith('payment-42');
    expect(mocks.submitMachineRequest.mock.invocationCallOrder[0]).toBeGreaterThan(mocks.waitForAuthorization.mock.invocationCallOrder[0]);
    expect((host.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);
    expect((host.querySelector('input[type="file"]') as HTMLInputElement).disabled).toBe(true);

    await act(async () => resolveRequest({
      requestId: 'server-request-42', status: 'pending', createdAt: '2026-09-22T10:00:00Z',
      documentCount: 3, totalPages: 777, additionalPages: 377, preparationTotal: 201.69,
    }));

    expect(host.textContent).toContain('Votre demande a bien été transmise.');
    expect(host.textContent).toContain('server-request-42');
    expect(host.textContent).toContain('777');
    expect(host.textContent).toContain('201,69');
    expect(host.textContent).not.toContain('12 pages');
    expect(host.querySelector('form')).toBeNull();
  });

  it('focuses and closes the Checkout window only after the request is confirmed', async () => {
    let resolveRequest!: (value: unknown) => void;
    mocks.submitMachineRequest.mockImplementation(() => new Promise(resolve => { resolveRequest = resolve; }));
    const focus = vi.mocked(window.focus);
    await renderPage();
    await completeForm();

    await submitTwice();
    expect(focus).not.toHaveBeenCalled();
    expect(checkoutPopup.close).not.toHaveBeenCalled();

    await act(async () => resolveRequest({
      requestId: 'server-request-42', status: 'pending', createdAt: '2026-09-22T10:00:00Z',
      documentCount: 1, totalPages: 12, additionalPages: 0, preparationTotal: 99.9,
    }));

    expect(host.textContent).toContain('Votre demande a bien été transmise.');
    expect(focus).toHaveBeenCalledTimes(1);
    expect(checkoutPopup.postMessage).toHaveBeenCalledWith(
      { type: 'diaglink:machine-request-submitted' }, window.location.origin);
    expect(checkoutPopup.close).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem('diaglink:initial-machine-payment-attempt')).toBeNull();
  });

  it('reuses the persisted idempotency key after a reload and a lost response', async () => {
    mocks.createPayment
      .mockRejectedValueOnce(new Error('Réponse perdue.'))
      .mockResolvedValueOnce({
        paymentRequestId: '11111111-1111-4111-8111-111111111111', status: 'pending', amount: 99.9,
        currency: 'EUR', checkoutUrl: 'https://checkout.stripe.test/session',
      });
    await renderPage();
    await completeForm();
    await submitTwice();

    expect(JSON.parse(localStorage.getItem('diaglink:initial-machine-payment-attempt')!)).toMatchObject({
      idempotencyKey: '11111111-1111-4111-8111-111111111111',
      totalPages: 12,
      email: 'claire@example.com',
    });

    await act(async () => root.unmount());
    host.remove();
    await renderPage();
    await completeForm();
    await submitTwice();

    expect(mocks.createPayment).toHaveBeenNthCalledWith(
      1, 12, 'claire@example.com', '11111111-1111-4111-8111-111111111111');
    expect(mocks.createPayment).toHaveBeenNthCalledWith(
      2, 12, 'claire@example.com', '11111111-1111-4111-8111-111111111111');
  });

  it('replaces a persisted attempt when immutable parameters change', async () => {
    localStorage.setItem('diaglink:initial-machine-payment-attempt', JSON.stringify({
      idempotencyKey: '22222222-2222-4222-8222-222222222222',
      totalPages: 400,
      email: 'other@example.com',
      authorized: false,
    }));
    await renderPage();
    await completeForm();
    await submitTwice();

    expect(mocks.createPayment).toHaveBeenCalledWith(
      12, 'claire@example.com', '11111111-1111-4111-8111-111111111111');
  });

  it('keeps the completed form and displays a safe server error', async () => {
    mocks.submitMachineRequest.mockRejectedValue(new MachineRequestSubmissionError('Le fichier manual.pdf est un PDF invalide ou illisible.'));
    await renderPage();
    await completeForm();
    await submitTwice();

    expect(host.textContent).toContain('Le fichier manual.pdf est un PDF invalide ou illisible.');
    expect(host.querySelector('form')).not.toBeNull();
    expect((host.querySelector('[name="firstName"]') as HTMLInputElement).value).toBe('Claire');
    expect(host.textContent).toContain('manual.pdf');
    expect((host.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(false);
  });

  it('displays the public network retry message', async () => {
    mocks.submitMachineRequest.mockRejectedValue(new MachineRequestSubmissionError('Impossible de contacter le service pour le moment. Veuillez réessayer.', true));
    await renderPage();
    await completeForm();
    await submitTwice();

    expect(host.textContent).toContain('Impossible de contacter le service pour le moment. Veuillez réessayer.');
  });

  it('keeps files and reports an authorization polling error without uploading', async () => {
    mocks.waitForAuthorization.mockRejectedValue(new Error('Autorisation expirée.'));
    await renderPage();
    await completeForm();
    await submitTwice();

    expect(mocks.submitMachineRequest).not.toHaveBeenCalled();
    expect(host.textContent).toContain('Autorisation expirée.');
    expect(host.textContent).toContain('manual.pdf');
  });

  it('clears a persisted attempt when the payment is terminal and unusable', async () => {
    mocks.waitForAuthorization.mockRejectedValue(
      new MachineRequestPaymentTerminalError('Cette autorisation de paiement ne peut plus être utilisée.'));
    await renderPage();
    await completeForm();
    await submitTwice();

    expect(localStorage.getItem('diaglink:initial-machine-payment-attempt')).toBeNull();
    expect(host.textContent).toContain('Cette autorisation de paiement ne peut plus être utilisée.');
  });
});
