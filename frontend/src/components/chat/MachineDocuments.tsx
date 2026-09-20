import { useEffect, useState } from 'react';
import { Button, Popover, PopoverSurface, PopoverTrigger, Text, Tooltip, makeStyles } from '@fluentui/react-components';
import { DocumentPdfRegular } from '@fluentui/react-icons';
import { useAuth } from '../../hooks/useAuth';
import { useMediaQuery } from '../../hooks/useThemeProvider';
import { getMachineDocuments, openMachineDocument } from '../../services/machineService';
import type { MachineDocumentDto } from '../../types/machine';
import styles from './ChatInput.module.css';

const useStyles = makeStyles({
  surface: {
    width: 'min(320px, 80vw)',
    border: '1px solid rgba(255, 255, 255, 0.25)',
    maxHeight: '50vh', overflowY: 'auto', overflowX: 'hidden',
    '@media (max-width: 1000px)': {
      boxSizing: 'border-box', minWidth: 0,
      width: 'min(320px, calc(100vw - 24px))', maxWidth: 'calc(100vw - 24px)',
    },
  },
  documentButton: {
    justifyContent: 'flex-start', textAlign: 'left', overflowWrap: 'anywhere',
    '@media (max-width: 1000px)': {
      minWidth: 0, width: '100%', maxWidth: '100%', overflow: 'hidden',
      '& > .fui-Button__icon': { flexShrink: 0 },
    },
  },
  documentName: {
    '@media (max-width: 1000px)': {
      minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
    },
  },
});

// The parent keys this component by machineId, discarding all state on machine changes.
export function MachineDocuments({ machineId }: { machineId?: string }) {
  const popupStyles = useStyles();
  const { getAccessToken } = useAuth();
  const [open, setOpen] = useState(false);
  const isCompact = useMediaQuery('(max-width: 1000px)');
  const [tooltipVisible, setTooltipVisible] = useState(false);
  const [documents, setDocuments] = useState<MachineDocumentDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open || !machineId) return;
    const controller = new AbortController();
    void getMachineDocuments(getAccessToken, machineId, controller.signal).then(result => {
      if (controller.signal.aborted) return;
      setLoading(false);
      if (result.kind === 'success') setDocuments(result.data);
      else setError('Impossible de charger les documents.');
    });
    return () => controller.abort();
  }, [open, machineId, getAccessToken]);

  const handleOpenDocument = (documentId: string) => {
    if (!machineId) return;
    setError(null);
    void openMachineDocument(getAccessToken, machineId, documentId).catch(error => {
      setError(error instanceof Error ? error.message : "Impossible d'ouvrir le document.");
    });
  };

  return (
    <Popover positioning={isCompact ? { position: 'above', align: 'start', overflowBoundaryPadding: 12 } : 'above-start'} open={open} onOpenChange={(_, data) => {
      setDocuments([]);
      setError(null);
      setLoading(data.open);
      setOpen(data.open);
    }}>
      <PopoverTrigger disableButtonEnhancement>
        <Tooltip content="Documents" relationship="label" withArrow
          visible={!isCompact && tooltipVisible}
          onVisibleChange={(_, data) => setTooltipVisible(!isCompact && data.visible)}>
          <Button size="small" appearance="subtle" icon={<DocumentPdfRegular />} disabled={!machineId} aria-label="Documents">
            <span className={styles.documentsLabel}>Documents</span>
          </Button>
        </Tooltip>
      </PopoverTrigger>
      <PopoverSurface aria-label="Documents de la machine" className={popupStyles.surface}>
        <Text weight="semibold">Documents</Text>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4, marginTop: 8 }}>
          {loading && <Text role="status">Chargement...</Text>}
          {error && <Text role="alert">{error}</Text>}
          {!loading && !error && documents.length === 0 && <Text>Aucun document disponible</Text>}
          {!loading && documents.map(document => (
            <Button key={document.id} appearance="subtle" icon={<DocumentPdfRegular />}
              className={popupStyles.documentButton} title={document.name}
              onClick={() => handleOpenDocument(document.id)}>
              <span className={popupStyles.documentName}>{document.name}</span>
            </Button>
          ))}
        </div>
      </PopoverSurface>
    </Popover>
  );
}
