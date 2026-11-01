import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TechnicalVisualGallery } from '../TechnicalVisualGallery';

const visual = {
  id: 42,
  documentId: 'manual',
  page: 58,
  assetType: 'full' as const,
  tile: null,
  name: 'page.png',
  displayOrder: 0,
};

describe('TechnicalVisualGallery', () => {
  let host: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    host = document.createElement('div');
    document.body.append(host);
    root = createRoot(host);
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:technical-visual');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 800 });
    Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, get: () => 600 });
    vi.stubGlobal('ResizeObserver', class {
      private readonly callback: ResizeObserverCallback;
      constructor(callback: ResizeObserverCallback) { this.callback = callback; }
      observe() { this.callback([], this as unknown as ResizeObserver); }
      disconnect() {}
    });
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    host.remove();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('opens the image viewer and provides bounded zoom controls', async () => {
    const loadVisual = vi.fn().mockResolvedValue(new Blob(['png'], { type: 'image/png' }));
    await act(async () => root.render(
      <TechnicalVisualGallery visuals={[visual]} loadVisual={loadVisual} />
    ));
    await act(async () => {
      await vi.waitFor(() => expect(loadVisual).toHaveBeenCalledWith(42, expect.any(AbortSignal)));
      await vi.waitFor(() => expect(document.querySelector('button[aria-label^="Agrandir illustration"]')).not.toBeNull());
    });

    const open = document.querySelector<HTMLButtonElement>('button[aria-label^="Agrandir illustration"]');
    await act(async () => open?.click());

    const dialog = document.body.querySelector('[role="dialog"]');
    const dialogImage = dialog?.querySelector<HTMLImageElement>('img');
    expect(dialogImage?.style.width).toBe('100%');
    expect(dialogImage?.draggable).toBe(false);
    expect(dialog?.textContent).toContain('100 %');

    const zoomIn = dialog?.querySelector<HTMLButtonElement>('button[aria-label="Agrandir"]');
    await act(async () => zoomIn?.click());
    expect(dialogImage?.style.width).toBe('125%');
    expect(dialog?.textContent).toContain('125 %');

    const reset = dialog?.querySelector<HTMLButtonElement>('button[aria-label="Taille initiale"]');
    await act(async () => reset?.click());
    expect(dialogImage?.style.width).toBe('100%');
    expect(reset?.disabled).toBe(true);

    for (let step = 0; step < 12; step += 1) {
      await act(async () => zoomIn?.click());
    }
    expect(dialogImage?.style.width).toBe('400%');
    expect(zoomIn?.disabled).toBe(true);
    expect(dialog?.querySelector('[data-technical-viewer="image"]')).not.toBeNull();
  });

  it('resets the zoom when the viewer is reopened', async () => {
    const loadVisual = vi.fn().mockResolvedValue(new Blob(['png'], { type: 'image/png' }));
    await act(async () => root.render(
      <TechnicalVisualGallery visuals={[visual]} loadVisual={loadVisual} />
    ));
    await act(async () => {
      await vi.waitFor(() => expect(document.querySelector('button[aria-label^="Agrandir illustration"]')).not.toBeNull());
    });

    const open = document.querySelector<HTMLButtonElement>('button[aria-label^="Agrandir illustration"]');
    await act(async () => open?.click());
    const zoomIn = document.body.querySelector<HTMLButtonElement>('button[aria-label="Agrandir"]');
    await act(async () => zoomIn?.click());
    expect(document.body.querySelector<HTMLImageElement>('[role="dialog"] img')?.style.width).toBe('125%');

    const close = document.body.querySelector<HTMLButtonElement>('button[aria-label="Fermer"]');
    await act(async () => close?.click());
    await act(async () => open?.click());
    expect(document.body.querySelector<HTMLImageElement>('[role="dialog"] img')?.style.width).toBe('100%');
  });

  it('fits large images in both dimensions without upscaling low-resolution images', async () => {
    const loadVisual = vi.fn().mockResolvedValue(new Blob(['png'], { type: 'image/png' }));
    await act(async () => root.render(
      <TechnicalVisualGallery visuals={[visual]} loadVisual={loadVisual} />
    ));
    await act(async () => {
      await vi.waitFor(() => expect(document.querySelector('button[aria-label^="Agrandir illustration"]')).not.toBeNull());
    });
    await act(async () => document.querySelector<HTMLButtonElement>('button[aria-label^="Agrandir illustration"]')?.click());
    const image = document.body.querySelector<HTMLImageElement>('[role="dialog"] img')!;
    Object.defineProperties(image, {
      naturalWidth: { configurable: true, value: 1600 },
      naturalHeight: { configurable: true, value: 1200 },
    });
    await act(async () => image.dispatchEvent(new Event('load')));
    expect(Number.parseFloat(image.style.width)).toBeLessThanOrEqual(776);

    Object.defineProperties(image, {
      naturalWidth: { configurable: true, value: 400 },
      naturalHeight: { configurable: true, value: 200 },
    });
    await act(async () => image.dispatchEvent(new Event('load')));
    expect(image.style.width).toBe('400px');
  });

  it('defers off-screen image downloads until the card approaches the viewport', async () => {
    let intersectionCallback: IntersectionObserverCallback = () => undefined;
    vi.stubGlobal('IntersectionObserver', class {
      constructor(callback: IntersectionObserverCallback) { intersectionCallback = callback; }
      observe() {}
      disconnect() {}
      unobserve() {}
      takeRecords() { return []; }
      root = null;
      rootMargin = '300px';
      thresholds = [0];
    });
    const loadVisual = vi.fn().mockResolvedValue(new Blob(['png'], { type: 'image/png' }));
    await act(async () => root.render(
      <TechnicalVisualGallery visuals={[visual]} loadVisual={loadVisual} />
    ));
    expect(loadVisual).not.toHaveBeenCalled();

    await act(async () => intersectionCallback(
      [{ isIntersecting: true } as IntersectionObserverEntry],
      {} as IntersectionObserver,
    ));
    await act(async () => vi.waitFor(() => expect(loadVisual).toHaveBeenCalledTimes(1)));
  });

  it('offers a retry when an illustration request fails', async () => {
    const loadVisual = vi.fn()
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce(new Blob(['png'], { type: 'image/png' }));
    await act(async () => root.render(
      <TechnicalVisualGallery visuals={[visual]} loadVisual={loadVisual} />
    ));
    await act(async () => vi.waitFor(() => expect(document.body.textContent).toContain('Illustration indisponible')));
    await act(async () => document.body.querySelector<HTMLButtonElement>('button')?.click());
    await act(async () => vi.waitFor(() => expect(loadVisual).toHaveBeenCalledTimes(2)));
  });
});
