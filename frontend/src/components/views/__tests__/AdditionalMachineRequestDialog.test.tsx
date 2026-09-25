import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdditionalMachineRequestDialog } from '../AdditionalMachineRequestDialog';

const api = vi.hoisted(() => ({ create: vi.fn(), wait: vi.fn(), submit: vi.fn() }));
vi.mock('../../../services/companyMachineRequestPaymentApi', () => ({
  createCompanyMachineRequestPayment: api.create,
  waitForCompanyMachineRequestAuthorization: api.wait,
  submitCompanyMachineRequest: api.submit,
}));
vi.mock('../../../pages/start/pdfSelection', async importOriginal => ({
  ...(await importOriginal<typeof import('../../../pages/start/pdfSelection')>()),
  inspectPdfFiles: async (files: File[]) => ({ accepted: files.map((file, id) => ({ file, id, pageCount: 416 })), errors: [] }),
}));

describe('AdditionalMachineRequestDialog', () => {
  beforeEach(() => {
    api.create.mockReset().mockResolvedValue({ paymentRequestId: 'payment-1', status: 'pending', amount: 134.12, currency: 'EUR', checkoutUrl: 'https://checkout.stripe.test' });
    api.wait.mockReset().mockResolvedValue({ paymentRequestId: 'payment-1', status: 'authorized', amount: 134.12, currency: 'EUR' });
    api.submit.mockReset().mockResolvedValue({ requestId: 'request-1', status: 'pending' });
    vi.stubGlobal('crypto', { randomUUID: vi.fn(() => 'stable-uuid') });
  });

  it('keeps machine data client-side, opens the named checkout and trusts server polling only', async () => {
    const popup = { location: { href: '' }, closed: false, close: vi.fn(), postMessage: vi.fn() };
    vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window);
    vi.spyOn(window, 'focus').mockImplementation(() => undefined);
    const container = document.createElement('div'); document.body.appendChild(container);
    const root = createRoot(container);
    await act(async () => root.render(<AdditionalMachineRequestDialog open onOpenChange={vi.fn()} getAccessToken={async () => 'token'} />));
    const inputs = Array.from(document.body.querySelectorAll('input'));
    await act(async () => {
      for (const [index, value] of ['Machine A', 'Atlas', 'H23'].entries()) {
        const input = inputs[index];
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value);
        input.dispatchEvent(new Event('input', { bubbles: true }));
      }
      const fileInput = inputs.find(input => input.type === 'file')!;
      Object.defineProperty(fileInput, 'files', { configurable: true, value: [new File(['pdf'], 'manual.pdf', { type: 'application/pdf' })] });
      fileInput.dispatchEvent(new Event('change', { bubbles: true }));
    });
    const authorize = Array.from(document.body.querySelectorAll('button')).find(button => button.textContent?.includes('Autoriser'))!;
    await act(async () => authorize.dispatchEvent(new MouseEvent('click', { bubbles: true })));

    expect(window.open).toHaveBeenCalledWith('', 'diaglink-machine-request-checkout');
    expect(api.create).toHaveBeenCalledWith(expect.any(Function), [expect.objectContaining({ name: 'manual.pdf' })], 'stable-uuid');
    expect(api.wait).toHaveBeenCalledWith(expect.any(Function), 'payment-1');
    expect(api.submit).toHaveBeenCalledWith(expect.any(Function), 'payment-1', expect.objectContaining({ machineName: 'Machine A', manufacturer: 'Atlas', model: 'H23' }), [expect.objectContaining({ name: 'manual.pdf' })]);
    expect(document.body.textContent).toContain('Demande envoyée');
    expect(document.body.textContent).toContain('vérifiée avant activation');
    expect(popup.close).toHaveBeenCalledOnce();
    expect(window.focus).toHaveBeenCalledOnce();
  });

  it('retries only the request upload after authorization while preserving payment and files', async () => {
    api.submit.mockRejectedValueOnce(new Error('Upload indisponible')).mockResolvedValueOnce({ requestId: 'request-1', status: 'pending' });
    const popup = { location: { href: '' }, closed: false, close: vi.fn(), postMessage: vi.fn() };
    vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window);
    const container = document.createElement('div'); document.body.appendChild(container);
    const root = createRoot(container);
    await act(async () => root.render(<AdditionalMachineRequestDialog open onOpenChange={vi.fn()} getAccessToken={async () => 'token'} />));
    const inputs = Array.from(document.body.querySelectorAll('input'));
    await act(async () => {
      for (const [index, value] of ['Machine B', 'Atlas', 'H23'].entries()) {
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(inputs[index], value);
        inputs[index].dispatchEvent(new Event('input', { bubbles: true }));
      }
      const fileInput = inputs.find(input => input.type === 'file')!;
      Object.defineProperty(fileInput, 'files', { configurable: true, value: [new File(['pdf'], 'retry.pdf', { type: 'application/pdf' })] });
      fileInput.dispatchEvent(new Event('change', { bubbles: true }));
    });
    await act(async () => Array.from(document.body.querySelectorAll('button')).find(button => button.textContent?.includes('Autoriser'))!.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(document.body.textContent).toContain('Paiement autorisé — l’envoi de la demande a échoué.');
    expect(document.body.textContent).toContain('Réessayer l’envoi');
    await act(async () => Array.from(document.body.querySelectorAll('button')).find(button => button.textContent?.includes('Réessayer'))!.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(api.create).toHaveBeenCalledTimes(1);
    expect(api.wait).toHaveBeenCalledTimes(1);
    expect(api.submit).toHaveBeenCalledTimes(2);
    expect(api.submit.mock.calls[1][1]).toBe('payment-1');
    expect(api.submit.mock.calls[1][3][0].name).toBe('retry.pdf');
    expect(document.body.textContent).toContain('Demande envoyée');
  });

  it('rejects a return message with the wrong origin and sends no browser status to polling', async () => {
    const popup = { location: { href: '' }, closed: false, close: vi.fn(), postMessage: vi.fn() };
    vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window);
    const container = document.createElement('div'); document.body.appendChild(container);
    const root = createRoot(container);
    await act(async () => root.render(<AdditionalMachineRequestDialog open onOpenChange={vi.fn()} getAccessToken={async () => 'token'} />));
    window.dispatchEvent(new MessageEvent('message', { origin: 'https://evil.test', source: popup as unknown as Window, data: { type: 'diaglink:machine-request-checkout-returned', status: 'authorized', paymentRequestId: 'secret' } }));
    expect(popup.postMessage).not.toHaveBeenCalled();
    expect(api.wait).not.toHaveBeenCalled();
  });
});
