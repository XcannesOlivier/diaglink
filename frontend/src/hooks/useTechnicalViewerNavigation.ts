import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type PointerEvent as ReactPointerEvent,
  type RefObject,
} from 'react';

interface Point {
  x: number;
  y: number;
}

interface PinchState {
  distance: number;
  zoom: number;
  contentX: number;
  contentY: number;
}

interface TechnicalViewerNavigationOptions {
  viewportRef: RefObject<HTMLElement | null>;
  zoom: number;
  minZoom: number;
  maxZoom: number;
  onZoomChange: (zoom: number) => void;
}

const clamp = (value: number, minimum: number, maximum: number) =>
  Math.min(maximum, Math.max(minimum, value));

const distance = (first: Point, second: Point) =>
  Math.hypot(second.x - first.x, second.y - first.y);

export function useTechnicalViewerNavigation({
  viewportRef,
  zoom,
  minZoom,
  maxZoom,
  onZoomChange,
}: TechnicalViewerNavigationOptions) {
  const pointersRef = useRef(new Map<number, Point>());
  const lastPointRef = useRef<Point | undefined>(undefined);
  const pinchRef = useRef<PinchState | undefined>(undefined);
  const zoomRef = useRef(zoom);
  const [isPannable, setIsPannable] = useState(false);
  const [isDragging, setIsDragging] = useState(false);
  const [isAtOrigin, setIsAtOrigin] = useState(true);
  const [viewportSize, setViewportSize] = useState({ width: 0, height: 0 });

  zoomRef.current = zoom;

  const measurePannable = useCallback(() => {
    const viewport = viewportRef.current;
    if (!viewport) return false;
    setViewportSize(current =>
      current.width === viewport.clientWidth && current.height === viewport.clientHeight
        ? current
        : { width: viewport.clientWidth, height: viewport.clientHeight });
    const maximumLeft = Math.max(0, viewport.scrollWidth - viewport.clientWidth);
    const maximumTop = Math.max(0, viewport.scrollHeight - viewport.clientHeight);
    const boundedLeft = clamp(viewport.scrollLeft, 0, maximumLeft);
    const boundedTop = clamp(viewport.scrollTop, 0, maximumTop);
    if (boundedLeft !== viewport.scrollLeft || boundedTop !== viewport.scrollTop) {
      if (typeof viewport.scrollTo === 'function') {
        viewport.scrollTo({ left: boundedLeft, top: boundedTop, behavior: 'auto' });
      } else {
        viewport.scrollLeft = boundedLeft;
        viewport.scrollTop = boundedTop;
      }
      setIsAtOrigin(boundedLeft === 0 && boundedTop === 0);
    }
    const pannable = viewport.scrollWidth > viewport.clientWidth + 1 ||
      viewport.scrollHeight > viewport.clientHeight + 1;
    setIsPannable(pannable);
    return pannable;
  }, [viewportRef]);

  const scrollWithinBounds = useCallback((left: number, top: number) => {
    const viewport = viewportRef.current;
    if (!viewport) return;
    const boundedLeft = clamp(left, 0, Math.max(0, viewport.scrollWidth - viewport.clientWidth));
    const boundedTop = clamp(top, 0, Math.max(0, viewport.scrollHeight - viewport.clientHeight));
    if (typeof viewport.scrollTo === 'function') {
      viewport.scrollTo({ left: boundedLeft, top: boundedTop, behavior: 'auto' });
    } else {
      viewport.scrollLeft = boundedLeft;
      viewport.scrollTop = boundedTop;
    }
    setIsAtOrigin(boundedLeft === 0 && boundedTop === 0);
  }, [viewportRef]);

  const resetView = useCallback(() => {
    pointersRef.current.clear();
    lastPointRef.current = undefined;
    pinchRef.current = undefined;
    setIsDragging(false);
    zoomRef.current = 1;
    onZoomChange(1);
    requestAnimationFrame(() => {
      scrollWithinBounds(0, 0);
      measurePannable();
    });
  }, [measurePannable, onZoomChange, scrollWithinBounds]);

  useEffect(() => {
    const frame = requestAnimationFrame(measurePannable);
    return () => cancelAnimationFrame(frame);
  }, [measurePannable, viewportSize.height, viewportSize.width, zoom]);

  useEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport) return;
    if (typeof ResizeObserver === 'undefined') {
      window.addEventListener('resize', measurePannable);
      return () => window.removeEventListener('resize', measurePannable);
    }
    const observer = new ResizeObserver(measurePannable);
    observer.observe(viewport);
    return () => observer.disconnect();
  }, [measurePannable, viewportRef]);

  useEffect(() => () => {
    pointersRef.current.clear();
  }, []);

  const localPoint = useCallback((first: Point, second?: Point) => {
    const viewport = viewportRef.current;
    const bounds = viewport?.getBoundingClientRect();
    const x = second ? (first.x + second.x) / 2 : first.x;
    const y = second ? (first.y + second.y) / 2 : first.y;
    return { x: x - (bounds?.left ?? 0), y: y - (bounds?.top ?? 0) };
  }, [viewportRef]);

  const beginPinch = useCallback(() => {
    const viewport = viewportRef.current;
    const points = [...pointersRef.current.values()];
    if (!viewport || points.length < 2) return;
    const midpoint = localPoint(points[0], points[1]);
    pinchRef.current = {
      distance: Math.max(1, distance(points[0], points[1])),
      zoom: zoomRef.current,
      contentX: (viewport.scrollLeft + midpoint.x) / zoomRef.current,
      contentY: (viewport.scrollTop + midpoint.y) / zoomRef.current,
    };
    setIsDragging(false);
  }, [localPoint, viewportRef]);

  const onPointerDown = useCallback((event: ReactPointerEvent<HTMLElement>) => {
    if (event.pointerType === 'mouse' && event.button !== 0) return;
    event.currentTarget.setPointerCapture?.(event.pointerId);
    pointersRef.current.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (pointersRef.current.size >= 2) {
      beginPinch();
      event.preventDefault();
      return;
    }
    lastPointRef.current = { x: event.clientX, y: event.clientY };
    if (measurePannable()) {
      setIsDragging(true);
      event.preventDefault();
    }
  }, [beginPinch, measurePannable]);

  const onPointerMove = useCallback((event: ReactPointerEvent<HTMLElement>) => {
    if (!pointersRef.current.has(event.pointerId)) return;
    const nextPoint = { x: event.clientX, y: event.clientY };
    pointersRef.current.set(event.pointerId, nextPoint);

    if (pointersRef.current.size >= 2) {
      const viewport = viewportRef.current;
      const points = [...pointersRef.current.values()];
      const pinch = pinchRef.current;
      if (!viewport || !pinch) return;
      event.preventDefault();
      const nextZoom = clamp(
        pinch.zoom * distance(points[0], points[1]) / pinch.distance,
        minZoom,
        maxZoom,
      );
      zoomRef.current = nextZoom;
      onZoomChange(nextZoom);
      const midpoint = localPoint(points[0], points[1]);
      requestAnimationFrame(() => {
        scrollWithinBounds(
          pinch.contentX * nextZoom - midpoint.x,
          pinch.contentY * nextZoom - midpoint.y,
        );
        measurePannable();
      });
      return;
    }

    const previous = lastPointRef.current;
    lastPointRef.current = nextPoint;
    if (!previous || !measurePannable()) {
      setIsDragging(false);
      return;
    }
    event.preventDefault();
    setIsDragging(true);
    const viewport = viewportRef.current;
    if (viewport) {
      scrollWithinBounds(
        viewport.scrollLeft - (nextPoint.x - previous.x),
        viewport.scrollTop - (nextPoint.y - previous.y),
      );
    }
  }, [localPoint, maxZoom, measurePannable, minZoom, onZoomChange, scrollWithinBounds, viewportRef]);

  const endPointer = useCallback((event: ReactPointerEvent<HTMLElement>) => {
    if (!pointersRef.current.has(event.pointerId)) return;
    pointersRef.current.delete(event.pointerId);
    event.currentTarget.releasePointerCapture?.(event.pointerId);
    pinchRef.current = undefined;
    const remaining = [...pointersRef.current.values()];
    lastPointRef.current = remaining[0];
    setIsDragging(remaining.length === 1 && measurePannable());
  }, [measurePannable]);

  const onScroll = useCallback(() => {
    const viewport = viewportRef.current;
    if (viewport) setIsAtOrigin(viewport.scrollLeft === 0 && viewport.scrollTop === 0);
  }, [viewportRef]);

  return {
    isPannable,
    isDragging,
    isAtInitialView: zoom === 1 && isAtOrigin,
    viewportSize,
    resetView,
    viewportPointerHandlers: {
      onPointerDown,
      onPointerMove,
      onPointerUp: endPointer,
      onPointerCancel: endPointer,
      onScroll,
    },
  };
}
