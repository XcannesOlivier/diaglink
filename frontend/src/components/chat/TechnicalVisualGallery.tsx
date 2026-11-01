import { useEffect, useRef, useState } from 'react';
import {
  Button,
  Dialog,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Spinner,
  Tooltip,
} from '@fluentui/react-components';
import { ArrowResetRegular, ZoomInRegular, ZoomOutRegular } from '@fluentui/react-icons';
import { DialogCloseButton } from '../core/DialogCloseButton';
import { useTechnicalViewerNavigation } from '../../hooks/useTechnicalViewerNavigation';
import type { TechnicalVisual } from '../../types/chat';
import styles from './TechnicalVisualGallery.module.css';

interface TechnicalVisualGalleryProps {
  visuals: TechnicalVisual[];
  loadVisual: (visualId: number, signal?: AbortSignal) => Promise<Blob>;
}

function TechnicalVisualCard({ visual, loadVisual }: { visual: TechnicalVisual; loadVisual: TechnicalVisualGalleryProps['loadVisual'] }) {
  const [imageUrl, setImageUrl] = useState<string>();
  const [failed, setFailed] = useState(false);
  const [shouldLoad, setShouldLoad] = useState(false);
  const [loadAttempt, setLoadAttempt] = useState(0);
  const [open, setOpen] = useState(false);
  const [zoom, setZoom] = useState(1);
  const [imageSize, setImageSize] = useState({ width: 0, height: 0 });
  const imageViewportRef = useRef<HTMLDivElement>(null);
  const cardRef = useRef<HTMLElement>(null);
  const navigation = useTechnicalViewerNavigation({
    viewportRef: imageViewportRef,
    zoom,
    minZoom: 0.5,
    maxZoom: 4,
    onZoomChange: setZoom,
  });
  const pageDescription = `page physique PDF ${visual.page}`;
  const tileDescription = visual.assetType === 'tile' && visual.tile ? `, zone ${visual.tile}` : '';
  const alt = `Illustration technique — ${pageDescription}${tileDescription}`;

  const openDialog = () => {
    setOpen(true);
    navigation.resetView();
  };

  const zoomButton = (
    label: string,
    icon: React.ReactElement,
    onClick: () => void,
    disabled = false,
  ) => (
    <Tooltip content={label} relationship="label">
      <Button type="button" appearance="subtle" icon={icon} aria-label={label} onClick={onClick} disabled={disabled} />
    </Tooltip>
  );

  useEffect(() => {
    const card = cardRef.current;
    if (!card || shouldLoad) return;
    if (typeof IntersectionObserver === 'undefined') {
      setShouldLoad(true);
      return;
    }
    const observer = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) {
        setShouldLoad(true);
        observer.disconnect();
      }
    }, { rootMargin: '300px' });
    observer.observe(card);
    return () => observer.disconnect();
  }, [shouldLoad]);

  useEffect(() => {
    if (!shouldLoad) return;
    const controller = new AbortController();
    let objectUrl: string | undefined;
    setImageUrl(undefined);
    setImageSize({ width: 0, height: 0 });
    setFailed(false);
    void loadVisual(visual.id, controller.signal)
      .then(blob => {
        if (controller.signal.aborted) return;
        objectUrl = URL.createObjectURL(blob);
        setImageUrl(objectUrl);
      })
      .catch(error => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setFailed(true);
      });
    return () => {
      controller.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [loadAttempt, loadVisual, shouldLoad, visual.id]);

  const retryLoad = () => {
    setFailed(false);
    setShouldLoad(true);
    setLoadAttempt(current => current + 1);
  };

  const availableWidth = Math.max(1, navigation.viewportSize.width - 24);
  const availableHeight = Math.max(1, navigation.viewportSize.height - 24);
  const fitScale = imageSize.width > 0 && imageSize.height > 0
    ? Math.min(1, availableWidth / imageSize.width, availableHeight / imageSize.height)
    : 1;
  const displayedWidth = imageSize.width > 0 ? imageSize.width * fitScale * zoom : undefined;

  return (
    <article ref={cardRef} className={styles.card} data-technical-visual-id={visual.id}>
      <div className={styles.preview}>
        {failed ? (
          <div className={styles.status} role="alert">
            <span>Illustration indisponible</span>
            <Button size="small" appearance="secondary" onClick={retryLoad}>Réessayer</Button>
          </div>
        ) : imageUrl ? (
          <button type="button" className={styles.imageButton} onClick={openDialog} aria-label={`Agrandir ${alt.toLowerCase()}`}>
            <img src={imageUrl} alt={alt} className={styles.image} onError={() => setFailed(true)} />
          </button>
        ) : shouldLoad ? (
          <span className={styles.status} role="status"><Spinner size="tiny" /> Chargement de l’illustration…</span>
        ) : (
          <button type="button" className={styles.loadButton} onClick={() => setShouldLoad(true)}>Charger l’illustration</button>
        )}
      </div>
      <div className={styles.caption}>
        <span>Page physique PDF {visual.page}</span>
        <span>{visual.assetType === 'tile' ? `Vue détaillée${visual.tile ? ` · ${visual.tile}` : ''}` : 'Page complète'}</span>
      </div>
      <Dialog open={open} onOpenChange={(_event, data) => setOpen(data.open)}>
        <DialogSurface className={styles.dialog}>
          <DialogBody className={styles.dialogBody}>
            <DialogTitle className={styles.title} action={<DialogCloseButton onClick={() => setOpen(false)} />}>
              <span className={styles.titleText}>Illustration technique — {visual.name}</span>
            </DialogTitle>
            <DialogContent className={styles.dialogContent}>
              <div className={styles.toolbar} aria-label="Zoom de l’illustration">
                <span className={styles.dialogCaption}>Page physique PDF {visual.page}{tileDescription}</span>
                <span className={styles.toolbarSpacer} />
                {zoomButton('Réduire', <ZoomOutRegular />, () => setZoom(current => Math.max(0.5, current - 0.25)), zoom <= 0.5)}
                {zoomButton('Taille initiale', <ArrowResetRegular />, navigation.resetView, navigation.isAtInitialView)}
                <span className={styles.zoomValue} aria-live="polite">{Math.round(zoom * 100)} %</span>
                {zoomButton('Agrandir', <ZoomInRegular />, () => setZoom(current => Math.min(4, current + 0.25)), zoom >= 4)}
              </div>
              <div
                data-technical-viewer="image"
                ref={imageViewportRef}
                className={`${styles.imageViewport} ${navigation.isPannable ? styles.pannable : ''} ${navigation.isDragging ? styles.dragging : ''}`}
                {...navigation.viewportPointerHandlers}
              >
                {failed && (
                  <div className={styles.dialogError} role="alert">
                    <span>L’illustration ne peut pas être affichée.</span>
                    <Button appearance="secondary" onClick={retryLoad}>Réessayer</Button>
                  </div>
                )}
                {imageUrl && !failed && (
                  <img
                    src={imageUrl}
                    alt={alt}
                    draggable={false}
                    className={styles.dialogImage}
                    style={{ width: displayedWidth === undefined ? `${zoom * 100}%` : `${displayedWidth}px` }}
                    onLoad={event => setImageSize({
                      width: event.currentTarget.naturalWidth,
                      height: event.currentTarget.naturalHeight,
                    })}
                    onError={() => setFailed(true)}
                  />
                )}
              </div>
            </DialogContent>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </article>
  );
}

export function TechnicalVisualGallery({ visuals, loadVisual }: TechnicalVisualGalleryProps) {
  return <section className={styles.gallery} aria-label="Illustrations techniques">
    {visuals.map(visual => <TechnicalVisualCard key={visual.id} visual={visual} loadVisual={loadVisual} />)}
  </section>;
}
