import { useEffect, useState } from 'react';
import { Dialog, DialogBody, DialogContent, DialogSurface, DialogTitle, Spinner } from '@fluentui/react-components';
import { DialogCloseButton } from '../core/DialogCloseButton';
import type { TechnicalVisual } from '../../types/chat';
import styles from './TechnicalVisualGallery.module.css';

interface TechnicalVisualGalleryProps {
  visuals: TechnicalVisual[];
  loadVisual: (visualId: number, signal?: AbortSignal) => Promise<Blob>;
}

function TechnicalVisualCard({ visual, loadVisual }: { visual: TechnicalVisual; loadVisual: TechnicalVisualGalleryProps['loadVisual'] }) {
  const [imageUrl, setImageUrl] = useState<string>();
  const [failed, setFailed] = useState(false);
  const [open, setOpen] = useState(false);
  const alt = `Illustration technique — page ${visual.page}`;

  useEffect(() => {
    const controller = new AbortController();
    let objectUrl: string | undefined;
    setImageUrl(undefined);
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
  }, [loadVisual, visual.id]);

  return (
    <article className={styles.card} data-technical-visual-id={visual.id}>
      <div className={styles.preview}>
        {failed ? <span className={styles.status}>Illustration indisponible</span> : imageUrl ? (
          <button type="button" className={styles.imageButton} onClick={() => setOpen(true)} aria-label={`Agrandir ${alt.toLowerCase()}`}>
            <img src={imageUrl} alt={alt} className={styles.image} />
          </button>
        ) : <span className={styles.status} role="status"><Spinner size="tiny" /> Chargement de l’illustration…</span>}
      </div>
      <div className={styles.caption}>
        <span>Page {visual.page}</span>
        <span>{visual.assetType === 'tile' ? 'Vue détaillée' : 'Page complète'}</span>
      </div>
      <Dialog open={open} onOpenChange={(_event, data) => setOpen(data.open)}>
        <DialogSurface className={styles.dialog}>
          <DialogBody>
            <DialogTitle action={<DialogCloseButton onClick={() => setOpen(false)} />}>Illustration technique</DialogTitle>
            <DialogContent className={styles.dialogContent}>
              <p className={styles.dialogCaption}>Page {visual.page}</p>
              {imageUrl && <img src={imageUrl} alt={alt} className={styles.dialogImage} />}
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
