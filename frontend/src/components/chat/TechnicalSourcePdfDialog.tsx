import { useEffect, useRef, useState } from 'react';
import {
  Button,
  Dialog,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Spinner,
  Text,
  Tooltip,
} from '@fluentui/react-components';
import {
  ArrowResetRegular,
  ChevronLeftRegular,
  ChevronRightRegular,
  ZoomInRegular,
  ZoomOutRegular,
} from '@fluentui/react-icons';
import { getDocument, GlobalWorkerOptions, type PDFDocumentProxy, type PDFDocumentLoadingTask, type RenderTask } from 'pdfjs-dist';
import pdfWorkerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url';
import type { TechnicalSourceReference } from '../../types/chat';
import { DialogCloseButton } from '../core/DialogCloseButton';
import { useTechnicalViewerNavigation } from '../../hooks/useTechnicalViewerNavigation';
import styles from './TechnicalSourcePdfDialog.module.css';

GlobalWorkerOptions.workerSrc = pdfWorkerUrl;

export function createPdfJsWasmUrl(baseUrl: string) {
  const normalizedBaseUrl = baseUrl.endsWith('/') ? baseUrl : `${baseUrl}/`;
  return `${normalizedBaseUrl}pdfjs-wasm/`;
}

const pdfJsWasmUrl = createPdfJsWasmUrl(import.meta.env.BASE_URL);

interface TechnicalSourcePdfDialogProps {
  source: TechnicalSourceReference;
  loadPdf: (sourceReferenceId: number, signal?: AbortSignal) => Promise<Blob>;
  onClose: () => void;
}

export function TechnicalSourcePdfDialog({ source, loadPdf, onClose }: TechnicalSourcePdfDialogProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const viewportRef = useRef<HTMLDivElement>(null);
  const [document, setDocument] = useState<PDFDocumentProxy>();
  const [pageNumber, setPageNumber] = useState(source.pdfPage);
  const [zoom, setZoom] = useState(1);
  const [renderZoom, setRenderZoom] = useState(1);
  const [loading, setLoading] = useState(true);
  const [rendering, setRendering] = useState(false);
  const [error, setError] = useState<string>();
  const [retry, setRetry] = useState(0);
  const navigation = useTechnicalViewerNavigation({
    viewportRef,
    zoom,
    minZoom: 0.5,
    maxZoom: 4,
    onZoomChange: setZoom,
  });

  useEffect(() => {
    const timeout = window.setTimeout(() => setRenderZoom(zoom), 120);
    return () => window.clearTimeout(timeout);
  }, [zoom]);

  useEffect(() => {
    const controller = new AbortController();
    let loadingTask: PDFDocumentLoadingTask | undefined;
    setLoading(true);
    setError(undefined);
    setDocument(undefined);
    setPageNumber(source.pdfPage);
    setRenderZoom(1);
    navigation.resetView();

    void loadPdf(source.id, controller.signal)
      .then(blob => blob.arrayBuffer())
      .then(data => {
        if (controller.signal.aborted) return undefined;
        loadingTask = getDocument({
          data: new Uint8Array(data),
          wasmUrl: pdfJsWasmUrl,
        });
        return loadingTask.promise;
      })
      .then(pdf => {
        if (!pdf || controller.signal.aborted) return;
        if (source.pdfPage > pdf.numPages) throw new Error('La page source n’est plus disponible.');
        setDocument(pdf);
        setLoading(false);
      })
      .catch(caught => {
        if (controller.signal.aborted) return;
        setLoading(false);
        setError(caught instanceof Error ? caught.message : 'Le document source n’a pas pu être chargé.');
      });

    return () => {
      controller.abort();
      void loadingTask?.destroy();
    };
  }, [loadPdf, navigation.resetView, retry, source.id, source.pdfPage]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!document || !canvas || navigation.viewportSize.width <= 0 || navigation.viewportSize.height <= 0) return;
    let cancelled = false;
    let renderTask: RenderTask | undefined;
    setRendering(true);
    setError(undefined);

    void document.getPage(pageNumber)
      .then(page => {
        if (cancelled) return;
        const baseViewport = page.getViewport({ scale: 1 });
        const availableWidth = Math.max(1, navigation.viewportSize.width - 24);
        const availableHeight = Math.max(1, navigation.viewportSize.height - 24);
        const fitScale = Math.max(0.05, Math.min(
          2,
          availableWidth / baseViewport.width,
          availableHeight / baseViewport.height,
        ));
        const viewport = page.getViewport({ scale: fitScale * renderZoom });
        const outputScale = Math.min(window.devicePixelRatio || 1, 2);
        const context = canvas.getContext('2d', { alpha: false });
        if (!context) throw new Error('Le document ne peut pas être affiché dans ce navigateur.');
        canvas.width = Math.floor(viewport.width * outputScale);
        canvas.height = Math.floor(viewport.height * outputScale);
        canvas.style.width = `${Math.floor(viewport.width)}px`;
        canvas.style.height = `${Math.floor(viewport.height)}px`;
        renderTask = page.render({
          canvas,
          canvasContext: context,
          viewport,
          transform: outputScale === 1 ? undefined : [outputScale, 0, 0, outputScale, 0, 0],
        });
        return renderTask.promise;
      })
      .then(() => {
        if (!cancelled) setRendering(false);
      })
      .catch(caught => {
        if (cancelled || (caught instanceof Error && caught.name === 'RenderingCancelledException')) return;
        setRendering(false);
        setError('Cette page du document n’a pas pu être affichée.');
      });

    return () => {
      cancelled = true;
      renderTask?.cancel();
    };
  }, [document, navigation.viewportSize.height, navigation.viewportSize.width, pageNumber, renderZoom]);

  const iconButton = (
    label: string,
    icon: React.ReactElement,
    onClick: () => void,
    disabled: boolean,
  ) => (
    <Tooltip content={label} relationship="label">
      <Button appearance="subtle" icon={icon} aria-label={label} onClick={onClick} disabled={disabled} />
    </Tooltip>
  );

  return (
    <Dialog open onOpenChange={(_event, data) => !data.open && onClose()}>
      <DialogSurface className={styles.dialog}>
        <DialogBody className={styles.body}>
          <DialogTitle
            className={styles.title}
            action={(
              <div className={styles.closeAction}>
                <DialogCloseButton onClick={onClose} />
              </div>
            )}
          >
            <span className={styles.titleText}>Source {source.label} — {source.documentName || 'Document constructeur'}</span>
          </DialogTitle>
          <DialogContent className={styles.content}>
            {document && (
              <div className={styles.toolbar} aria-label="Navigation du document source">
                {iconButton('Page précédente', <ChevronLeftRegular />, () => setPageNumber(current => Math.max(1, current - 1)), pageNumber <= 1 || rendering)}
                {iconButton('Page suivante', <ChevronRightRegular />, () => setPageNumber(current => Math.min(document.numPages, current + 1)), pageNumber >= document.numPages || rendering)}
                <span className={styles.pageIndicator} aria-live="polite" aria-label={`Page PDF ${pageNumber} sur ${document.numPages}`}>
                  PDF {pageNumber}/{document.numPages}
                </span>
                <span className={styles.toolbarSpacer} />
                {iconButton('Réduire', <ZoomOutRegular />, () => setZoom(current => Math.max(0.5, current - 0.25)), zoom <= 0.5 || rendering)}
                {iconButton('Vue initiale', <ArrowResetRegular />, navigation.resetView, navigation.isAtInitialView || rendering)}
                {iconButton('Agrandir', <ZoomInRegular />, () => setZoom(current => Math.min(4, current + 0.25)), zoom >= 4 || rendering)}
              </div>
            )}
            <div
              data-technical-viewer="pdf"
              className={`${styles.viewport} ${navigation.isPannable ? styles.pannable : ''} ${navigation.isDragging ? styles.dragging : ''}`}
              ref={viewportRef}
              {...navigation.viewportPointerHandlers}
              aria-busy={loading || rendering}
            >
              {loading && <Spinner label="Chargement du document source…" />}
              {!loading && rendering && <Spinner className={styles.renderSpinner} size="small" label="Affichage de la page…" />}
              {error && (
                <div className={styles.error} role="alert">
                  <Text>{error}</Text>
                  {!document && <Button appearance="secondary" onClick={() => setRetry(current => current + 1)}>Réessayer</Button>}
                </div>
              )}
              <canvas
                ref={canvasRef}
                className={`${document && !error ? styles.canvas : styles.hiddenCanvas} ${rendering ? styles.renderingCanvas : ''}`}
                aria-label={pageNumber === source.pdfPage
                  ? `Page PDF ${pageNumber}, page imprimée ${source.displayPage}`
                  : `Page PDF ${pageNumber}`}
              />
            </div>
          </DialogContent>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
