import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TechnicalSourcePdfDialog } from '../TechnicalSourcePdfDialog';

const pdf = vi.hoisted(() => ({
  getDocument: vi.fn(),
  getPage: vi.fn(),
  render: vi.fn(),
  destroy: vi.fn(),
}));

vi.mock('pdfjs-dist', () => ({
  GlobalWorkerOptions: {},
  getDocument: pdf.getDocument,
}));
vi.mock('pdfjs-dist/build/pdf.worker.min.mjs?url', () => ({ default: 'pdf.worker.mjs' }));

const source = {
  id: 123,
  pdfPage: 74,
  displayPage: '72',
  label: 'p. 72',
  startIndex: 9,
  endIndex: 14,
  displayOrder: 0,
};

describe('TechnicalSourcePdfDialog', () => {
  let host: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    host = document.createElement('div');
    document.body.append(host);
    root = createRoot(host);
    pdf.getDocument.mockReset();
    pdf.getPage.mockReset();
    pdf.render.mockReset();
    pdf.destroy.mockReset();
    pdf.render.mockReturnValue({ promise: Promise.resolve(), cancel: vi.fn() });
    pdf.getPage.mockResolvedValue({
      getViewport: ({ scale }: { scale: number }) => ({ width: 600 * scale, height: 800 * scale }),
      render: pdf.render,
    });
    pdf.getDocument.mockReturnValue({
      promise: Promise.resolve({ numPages: 100, getPage: pdf.getPage }),
      destroy: pdf.destroy,
    });
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({} as CanvasRenderingContext2D);
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 800 });
    vi.stubGlobal('ResizeObserver', class {
      observe() {}
      disconnect() {}
    });
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    host.remove();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('loads by opaque id and renders pdfPage without displaying the physical number', async () => {
    const loadPdf = vi.fn().mockResolvedValue({
      type: 'application/pdf',
      arrayBuffer: async () => new ArrayBuffer(4),
    } as Blob);
    await act(async () => root.render(
      <TechnicalSourcePdfDialog source={source} loadPdf={loadPdf} onClose={vi.fn()} />
    ));
    await act(async () => {
      await vi.waitFor(() => expect(pdf.getPage).toHaveBeenCalledWith(74));
    });

    expect(loadPdf).toHaveBeenCalledWith(123, expect.any(AbortSignal));
    expect(document.body.textContent).toContain('Source p. 72');
    expect(document.body.textContent).not.toContain('74');
    expect(document.body.querySelector('canvas')?.getAttribute('aria-label')).toBe('Source page 72');
  });

  it('shows a safe loading failure and supports retry', async () => {
    const loadPdf = vi.fn().mockRejectedValue(new Error('Cette source n’est plus disponible.'));
    await act(async () => root.render(
      <TechnicalSourcePdfDialog source={source} loadPdf={loadPdf} onClose={vi.fn()} />
    ));
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });

    expect(document.body.querySelector('[role="alert"]')?.textContent).toContain('Cette source n’est plus disponible.');
    const retry = [...document.body.querySelectorAll('button')].find(button => button.textContent === 'Réessayer');
    await act(async () => retry?.click());
    expect(loadPdf).toHaveBeenCalledTimes(2);
  });
});