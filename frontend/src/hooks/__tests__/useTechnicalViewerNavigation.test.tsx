import { act, useLayoutEffect, useRef, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useTechnicalViewerNavigation } from '../useTechnicalViewerNavigation';

interface HarnessProps {
  clientWidth?: number;
  clientHeight?: number;
  scrollWidth?: number;
  scrollHeight?: number;
}

function Harness({
  clientWidth = 200,
  clientHeight = 160,
  scrollWidth = 600,
  scrollHeight = 500,
}: HarnessProps) {
  const viewportRef = useRef<HTMLDivElement>(null);
  const [zoom, setZoom] = useState(1);
  const navigation = useTechnicalViewerNavigation({
    viewportRef,
    zoom,
    minZoom: 0.5,
    maxZoom: 4,
    onZoomChange: setZoom,
  });

  useLayoutEffect(() => {
    const viewport = viewportRef.current!;
    Object.defineProperties(viewport, {
      clientWidth: { configurable: true, value: clientWidth },
      clientHeight: { configurable: true, value: clientHeight },
      scrollWidth: { configurable: true, value: scrollWidth },
      scrollHeight: { configurable: true, value: scrollHeight },
    });
    viewport.scrollTo = vi.fn((optionsOrX?: ScrollToOptions | number, y?: number) => {
      const left = typeof optionsOrX === 'number' ? optionsOrX : optionsOrX?.left;
      const top = typeof optionsOrX === 'number' ? y : optionsOrX?.top;
      viewport.scrollLeft = left ?? viewport.scrollLeft;
      viewport.scrollTop = top ?? viewport.scrollTop;
      viewport.dispatchEvent(new Event('scroll'));
    }) as typeof viewport.scrollTo;
  }, [clientHeight, clientWidth, scrollHeight, scrollWidth]);

  return <>
    <div
      ref={viewportRef}
      data-testid="viewport"
      data-zoom={zoom}
      data-pannable={navigation.isPannable}
      data-dragging={navigation.isDragging}
      data-viewport-width={navigation.viewportSize.width}
      data-viewport-height={navigation.viewportSize.height}
      {...navigation.viewportPointerHandlers}
    />
    <button type="button" onClick={navigation.resetView}>Reset</button>
  </>;
}

function pointerEvent(
  type: string,
  { pointerId, pointerType = 'mouse', clientX, clientY, button = 0 }:
  { pointerId: number; pointerType?: string; clientX: number; clientY: number; button?: number },
) {
  const event = new MouseEvent(type, { bubbles: true, cancelable: true, clientX, clientY, button });
  Object.defineProperties(event, {
    pointerId: { value: pointerId },
    pointerType: { value: pointerType },
  });
  return event;
}

describe('useTechnicalViewerNavigation', () => {
  let host: HTMLDivElement;
  let root: Root;
  let resizeObserverCallback: ResizeObserverCallback;

  beforeEach(async () => {
    host = document.createElement('div');
    document.body.append(host);
    root = createRoot(host);
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      callback(0);
      return 1;
    });
    vi.stubGlobal('cancelAnimationFrame', vi.fn());
    vi.stubGlobal('ResizeObserver', class {
      private readonly callback: ResizeObserverCallback;
      constructor(callback: ResizeObserverCallback) {
        this.callback = callback;
        resizeObserverCallback = callback;
      }
      observe() { this.callback([], this as unknown as ResizeObserver); }
      disconnect() {}
    });
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    host.remove();
    vi.unstubAllGlobals();
  });

  const renderHarness = async (props: HarnessProps = {}) => {
    await act(async () => root.render(<Harness {...props} />));
    const viewport = host.querySelector<HTMLElement>('[data-testid="viewport"]')!;
    await act(async () => window.dispatchEvent(new Event('resize')));
    return viewport;
  };

  it('pans horizontally and vertically while the primary mouse button is held', async () => {
    const viewport = await renderHarness();
    viewport.scrollLeft = 100;
    viewport.scrollTop = 120;

    await act(async () => {
      viewport.dispatchEvent(pointerEvent('pointerdown', { pointerId: 1, clientX: 100, clientY: 100 }));
      viewport.dispatchEvent(pointerEvent('pointermove', { pointerId: 1, clientX: 70, clientY: 50 }));
    });

    expect(viewport.scrollLeft).toBe(130);
    expect(viewport.scrollTop).toBe(170);
    expect(viewport.dataset.dragging).toBe('true');
  });

  it('clamps panning to every document boundary', async () => {
    const viewport = await renderHarness();
    viewport.scrollLeft = 10;
    viewport.scrollTop = 10;
    await act(async () => {
      viewport.dispatchEvent(pointerEvent('pointerdown', { pointerId: 1, clientX: 50, clientY: 50 }));
      viewport.dispatchEvent(pointerEvent('pointermove', { pointerId: 1, clientX: 200, clientY: 200 }));
    });
    expect(viewport.scrollLeft).toBe(0);
    expect(viewport.scrollTop).toBe(0);

    await act(async () => {
      viewport.dispatchEvent(pointerEvent('pointermove', { pointerId: 1, clientX: -1000, clientY: -1000 }));
    });
    expect(viewport.scrollLeft).toBe(400);
    expect(viewport.scrollTop).toBe(340);
  });

  it('does not pan when the content fits inside the viewport', async () => {
    const viewport = await renderHarness({ scrollWidth: 200, scrollHeight: 160 });
    await act(async () => {
      viewport.dispatchEvent(pointerEvent('pointerdown', { pointerId: 1, clientX: 100, clientY: 100 }));
      viewport.dispatchEvent(pointerEvent('pointermove', { pointerId: 1, clientX: 20, clientY: 20 }));
    });

    expect(viewport.scrollLeft).toBe(0);
    expect(viewport.scrollTop).toBe(0);
    expect(viewport.dataset.pannable).toBe('false');
    expect(viewport.dataset.dragging).toBe('false');
  });

  it('supports centered touch pinch zoom followed by one-finger panning', async () => {
    const viewport = await renderHarness();
    viewport.scrollLeft = 50;
    viewport.scrollTop = 40;
    await act(async () => {
      viewport.dispatchEvent(pointerEvent('pointerdown', { pointerId: 1, pointerType: 'touch', clientX: 50, clientY: 60 }));
      viewport.dispatchEvent(pointerEvent('pointerdown', { pointerId: 2, pointerType: 'touch', clientX: 150, clientY: 60 }));
      viewport.dispatchEvent(pointerEvent('pointermove', { pointerId: 2, pointerType: 'touch', clientX: 250, clientY: 60 }));
    });

    expect(viewport.dataset.zoom).toBe('2');
    expect(viewport.scrollLeft).toBeGreaterThan(50);

    const beforePan = viewport.scrollLeft;
    await act(async () => {
      viewport.dispatchEvent(pointerEvent('pointerup', { pointerId: 2, pointerType: 'touch', clientX: 250, clientY: 60 }));
      viewport.dispatchEvent(pointerEvent('pointermove', { pointerId: 1, pointerType: 'touch', clientX: 20, clientY: 60 }));
    });
    expect(viewport.scrollLeft).toBeGreaterThan(beforePan);
  });

  it('returns zoom and scroll position to the initial view', async () => {
    const viewport = await renderHarness();
    viewport.scrollLeft = 80;
    viewport.scrollTop = 60;
    await act(async () => {
      viewport.dispatchEvent(new Event('scroll', { bubbles: true }));
      host.querySelector<HTMLButtonElement>('button')?.click();
    });

    expect(viewport.dataset.zoom).toBe('1');
    expect(viewport.scrollLeft).toBe(0);
    expect(viewport.scrollTop).toBe(0);
  });

  it('remeasures portrait and landscape viewports and clamps the document after an orientation change', async () => {
    const viewport = await renderHarness({
      clientWidth: 700,
      clientHeight: 900,
      scrollWidth: 1400,
      scrollHeight: 1800,
    });
    expect(viewport.dataset.viewportWidth).toBe('700');
    expect(viewport.dataset.viewportHeight).toBe('900');
    viewport.scrollLeft = 650;
    viewport.scrollTop = 850;

    await act(async () => {
      root.render(<Harness clientWidth={1100} clientHeight={650} scrollWidth={1200} scrollHeight={800} />);
    });
    await act(async () => resizeObserverCallback([], {} as ResizeObserver));

    expect(viewport.dataset.viewportWidth).toBe('1100');
    expect(viewport.dataset.viewportHeight).toBe('650');
    expect(viewport.scrollLeft).toBe(100);
    expect(viewport.scrollTop).toBe(150);
  });
});
