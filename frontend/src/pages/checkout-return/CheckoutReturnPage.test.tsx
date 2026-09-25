import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CheckoutReturnPage } from './CheckoutReturnPage';
import { checkoutReturnedMessage, checkoutWindowName } from '../start/checkoutPopup';

let root: Root | null = null;
let container: HTMLDivElement | null = null;
let originalOpener: Window | null;

async function renderPage() {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  await act(async () => root?.render(<CheckoutReturnPage />));
}

beforeEach(() => {
  originalOpener = window.opener;
  window.name = '';
  Object.defineProperty(window, 'opener', { configurable: true, value: null });
});

afterEach(async () => {
  if (root) await act(async () => root?.unmount());
  container?.remove();
  root = null;
  container = null;
  window.name = '';
  Object.defineProperty(window, 'opener', { configurable: true, value: originalOpener });
  vi.restoreAllMocks();
});

describe('/app/checkout-return', () => {
  it('notifies the same-origin opener with the exact return message, then closes', async () => {
    const postMessage = vi.fn();
    const opener = { postMessage } as unknown as Window;
    window.name = checkoutWindowName;
    Object.defineProperty(window, 'opener', { configurable: true, value: opener });
    const close = vi.spyOn(window, 'close').mockImplementation(() => undefined);

    await renderPage();

    expect(opener.postMessage).toHaveBeenCalledOnce();
    expect(opener.postMessage).toHaveBeenCalledWith(
      { type: checkoutReturnedMessage }, window.location.origin);
    expect(close).toHaveBeenCalledOnce();
    expect(postMessage.mock.invocationCallOrder[0]).toBeLessThan(close.mock.invocationCallOrder[0]);
    expect(container?.childElementCount).toBe(0);
  });

  it('does not notify or close when the window name is not the checkout name', async () => {
    const opener = { postMessage: vi.fn() } as unknown as Window;
    Object.defineProperty(window, 'opener', { configurable: true, value: opener });
    const close = vi.spyOn(window, 'close').mockImplementation(() => undefined);

    await renderPage();

    expect(opener.postMessage).not.toHaveBeenCalled();
    expect(close).not.toHaveBeenCalled();
  });

  it('does not notify or close without an opener', async () => {
    window.name = checkoutWindowName;
    const close = vi.spyOn(window, 'close').mockImplementation(() => undefined);

    await renderPage();

    expect(close).not.toHaveBeenCalled();
  });
});
