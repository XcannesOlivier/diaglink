import { useEffect, useMemo, useRef, useState, type ChangeEvent, type DragEvent } from 'react';
import { Button, Dialog, DialogActions, DialogBody, DialogContent, DialogSurface, DialogTitle, Field, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { ArrowUpload24Regular } from '@fluentui/react-icons';
import { authorizationConfirmedMessage, checkoutReturnedMessage, checkoutWindowName, isTrustedCheckoutMessage } from '../../pages/start/checkoutPopup';
import { formatFileSize, inspectPdfFiles, type SelectedPdf } from '../../pages/start/pdfSelection';
import { cancelAdditionalDocumentsRequest, getAdditionalDocumentsPayment, stageAdditionalDocuments, startAdditionalDocumentsPayment, type AdditionalDocumentsStage } from '../../services/additionalDocumentsRequestApi';
import type { MachineDto } from '../../types/machine';
import { additionalDocumentsMinimumAmountCents, additionalDocumentsPricePerPageCents, calculateAdditionalDocumentsAmountCents } from './additionalDocumentsPricing';
import { DialogCloseButton } from '../core/DialogCloseButton';

const SHORT_POLL_ATTEMPTS = 6;
const SHORT_POLL_INTERVAL_MS = 1500;
const POPUP_CLOSED_CHECK_INTERVAL_MS = 500;
const recoveryStorageKey = (machineId: string) => `diaglink:additional-documents:${machineId}`;
const formatCents = (cents: number) => new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'EUR' }).format(cents / 100);

const useStyles = makeStyles({
  content: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, minWidth: 'min(560px, 80vw)' },
  dropzone: { padding: tokens.spacingVerticalL, border: `1px dashed ${tokens.colorBrandStroke1}`, borderRadius: tokens.borderRadiusMedium, textAlign: 'center', cursor: 'pointer' },
  hidden: { display: 'none' }, files: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  file: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: tokens.spacingHorizontalM },
  estimate: { display: 'grid', gridTemplateColumns: '1fr auto', gap: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalL}` },
  error: { color: tokens.colorPaletteRedForeground1 },
  success: { padding: tokens.spacingVerticalL, borderRadius: tokens.borderRadiusMedium, backgroundColor: tokens.colorPaletteGreenBackground1 },
  actions: { justifyContent: 'center', flexWrap: 'wrap' },
});

type Attempt = { key: string; stage?: AdditionalDocumentsStage };
type Phase = 'selecting' | 'staging' | 'checkout' | 'awaiting-return' | 'verifying' | 'pending' | 'authorized';

export function AdditionalDocumentsRequestDialog({ open, onOpenChange, machine, getAccessToken }: {
  open: boolean; onOpenChange: (open: boolean) => void; machine: MachineDto;
  getAccessToken: () => Promise<string | null>;
}) {
  const styles = useStyles(); const inputRef = useRef<HTMLInputElement>(null); const nextFileId = useRef(0);
  const attempt = useRef<Attempt | null>(null); const popup = useRef<Window | null>(null); const busy = useRef(false);
  const recoveredStatusChecked = useRef(false);
  const [pdfs, setPdfs] = useState<SelectedPdf[]>([]); const [fileErrors, setFileErrors] = useState<string[]>([]);
  const [reading, setReading] = useState(false); const [phase, setPhase] = useState<Phase>('selecting');
  const [stage, setStage] = useState<AdditionalDocumentsStage | null>(null); const [error, setError] = useState<string | null>(null);
  const [recoveredRequestId, setRecoveredRequestId] = useState<string | null>(null);
  const localPages = useMemo(() => pdfs.reduce((sum, pdf) => sum + pdf.pageCount, 0), [pdfs]);
  const localAmount = calculateAdditionalDocumentsAmountCents(localPages);
  const locked = stage !== null || recoveredRequestId !== null;

  const confirmAuthorized = () => {
    setPhase('authorized'); setError(null); window.focus();
    sessionStorage.removeItem(recoveryStorageKey(machine.id));
    setRecoveredRequestId(null);
    popup.current?.postMessage({ type: authorizationConfirmedMessage }, window.location.origin);
    popup.current?.close();
  };

  const checkAuthorization = async (poll: boolean) => {
    const requestId = attempt.current?.stage?.requestId ?? recoveredRequestId; if (!requestId || busy.current) return;
    busy.current = true; setPhase('verifying'); setError(null);
    try {
      const attempts = poll ? SHORT_POLL_ATTEMPTS : 1;
      for (let index = 0; index < attempts; index += 1) {
        const payment = await getAdditionalDocumentsPayment(getAccessToken, requestId);
        if (payment.status === 'authorized' || payment.status === 'captured') { confirmAuthorized(); return; }
        if (payment.status === 'cancelled' || payment.status === 'abandoned') {
          sessionStorage.removeItem(recoveryStorageKey(machine.id));
          attempt.current = null; setRecoveredRequestId(null); setStage(null); setPdfs([]); setPhase('selecting');
          return;
        }
        if (index + 1 < attempts) await new Promise(resolve => window.setTimeout(resolve, SHORT_POLL_INTERVAL_MS));
      }
      setPhase('pending');
    } catch (caught) {
      setPhase('pending');
      setError(caught instanceof Error ? caught.message : 'L’autorisation n’a pas pu être vérifiée.');
    } finally { busy.current = false; }
  };

  useEffect(() => {
    const receive = (event: MessageEvent) => {
      if (!isTrustedCheckoutMessage(event, popup.current, checkoutReturnedMessage)) return;
      void checkAuthorization(true);
    };
    window.addEventListener('message', receive);
    return () => window.removeEventListener('message', receive);
  });

  useEffect(() => {
    if (phase !== 'awaiting-return') return;
    const timer = window.setInterval(() => {
      if (popup.current?.closed) setPhase('pending');
    }, POPUP_CLOSED_CHECK_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [phase]);

  useEffect(() => {
    if (!open || !recoveredRequestId || recoveredStatusChecked.current) return;
    recoveredStatusChecked.current = true;
    void checkAuthorization(false);
  }, [open, recoveredRequestId]);

  useEffect(() => {
    if (!open) return;
    attempt.current = null; popup.current = null; recoveredStatusChecked.current = false; setStage(null); setRecoveredRequestId(null);
    setPdfs([]); setFileErrors([]); setError(null); setPhase('selecting');
    try {
      const raw = sessionStorage.getItem(recoveryStorageKey(machine.id));
      if (!raw) return;
      const saved = JSON.parse(raw) as { requestId?: unknown; machineId?: unknown };
      if (saved.machineId === machine.id && typeof saved.requestId === 'string' && saved.requestId.length > 0) {
        setRecoveredRequestId(saved.requestId);
        setPhase('verifying');
      }
    } catch {
      sessionStorage.removeItem(recoveryStorageKey(machine.id));
    }
  }, [machine.id, open]);

  const addFiles = async (files: File[]) => {
    if (!files.length || locked || busy.current) return;
    setReading(true); const result = await inspectPdfFiles(files, pdfs, () => nextFileId.current++);
    setPdfs(current => [...current, ...result.accepted]); setFileErrors(result.errors);
    attempt.current = null; setError(null); setReading(false);
  };

  const continueToPayment = async () => {
    if (recoveredRequestId && phase === 'pending') {
      if (busy.current) return;
      busy.current = true; setError(null);
      try {
        popup.current = window.open('', checkoutWindowName);
        if (!popup.current) throw new Error('Autorisez l’ouverture de la fenêtre Stripe pour poursuivre.');
        const payment = await startAdditionalDocumentsPayment(getAccessToken, recoveredRequestId);
        if (!payment.checkoutUrl) throw new Error('La page de paiement Stripe est indisponible.');
        popup.current.location.href = payment.checkoutUrl;
        setPhase('awaiting-return');
      } catch (caught) {
        popup.current?.close();
        setPhase('pending');
        setError(caught instanceof Error ? caught.message : 'La demande n’a pas pu être préparée.');
      } finally { busy.current = false; }
      return;
    }
    if (busy.current || reading || fileErrors.length || pdfs.length === 0) {
      setError('Sélectionnez au moins un PDF valide.'); return;
    }
    busy.current = true; setError(null);
    try {
      popup.current = window.open('', checkoutWindowName);
      if (!popup.current) { setPhase('pending'); throw new Error('Autorisez l’ouverture de la fenêtre Stripe pour poursuivre.'); }
      if (!attempt.current) attempt.current = { key: crypto.randomUUID() };
      if (!attempt.current.stage) {
        setPhase('staging');
        attempt.current.stage = await stageAdditionalDocuments(getAccessToken, machine.id,
          pdfs.map(pdf => pdf.file), attempt.current.key);
        setStage(attempt.current.stage);
        sessionStorage.setItem(recoveryStorageKey(machine.id), JSON.stringify({
          requestId: attempt.current.stage.requestId,
          machineId: machine.id,
        }));
        if (attempt.current.stage.totalPages !== localPages || attempt.current.stage.amountCents !== localAmount) {
          popup.current.close();
          setPhase('selecting');
          return;
        }
      }
      setPhase('checkout');
      const payment = await startAdditionalDocumentsPayment(getAccessToken, attempt.current.stage.requestId);
      if (!payment.checkoutUrl) throw new Error('La page de paiement Stripe est indisponible.');
      popup.current.location.href = payment.checkoutUrl;
      setPhase('awaiting-return');
    } catch (caught) {
      popup.current?.close();
      if (phase !== 'pending') setPhase(attempt.current?.stage ? 'pending' : 'selecting');
      setError(caught instanceof Error ? caught.message : 'La demande n’a pas pu être préparée.');
    } finally { busy.current = false; }
  };

  const cancelRequest = async () => {
    const requestId = attempt.current?.stage?.requestId ?? recoveredRequestId;
    if (!requestId || busy.current || !window.confirm('Annuler cette demande ?\nVous pourrez sélectionner de nouveaux documents ensuite.')) return;
    busy.current = true; setError(null);
    try {
      await cancelAdditionalDocumentsRequest(getAccessToken, requestId);
      popup.current?.close(); popup.current = null; attempt.current = null; recoveredStatusChecked.current = false;
      sessionStorage.removeItem(recoveryStorageKey(machine.id));
      setRecoveredRequestId(null); setStage(null); setPdfs([]); setFileErrors([]); setPhase('selecting');
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'La demande n’a pas pu être annulée.');
    } finally { busy.current = false; }
  };

  const selectFiles = (event: ChangeEvent<HTMLInputElement>) => { void addFiles(Array.from(event.target.files ?? [])); event.target.value = ''; };
  const drop = (event: DragEvent<HTMLDivElement>) => { event.preventDefault(); void addFiles(Array.from(event.dataTransfer.files)); };
  const serverPages = stage?.totalPages ?? localPages; const serverAmount = stage?.amountCents ?? localAmount;

  return <Dialog open={open} onOpenChange={(_event, data) => !busy.current && onOpenChange(data.open)}><DialogSurface>
    <DialogTitle action={<DialogCloseButton disabled={busy.current} onClick={() => onOpenChange(false)} />}>Demander l’ajout de documents</DialogTitle><DialogBody><DialogContent className={styles.content}>
      <Text>Machine</Text><Text weight="semibold">{machine.name}</Text>
      {phase === 'authorized' ? <div className={styles.success} role="status">
        <Text block weight="semibold" size={500}>Demande envoyée</Text>
        <Text block>Vos documents ont bien été reçus. Votre demande va être vérifiée avant leur ajout à la machine.</Text>
        {stage && <Text block>{stage.documentCount} document(s) — {serverPages} page(s)</Text>}
        {stage && <Text block>Montant autorisé : {formatCents(serverAmount)} HT. Il ne sera encaissé qu’après validation de votre demande.</Text>}
      </div> : <>
        {!recoveredRequestId && <>
        <Field label="Documents PDF" required><div className={styles.dropzone} role="button" tabIndex={0}
          aria-disabled={locked} onClick={() => !locked && inputRef.current?.click()}
          onKeyDown={event => event.key === 'Enter' && !locked && inputRef.current?.click()}
          onDragOver={event => event.preventDefault()} onDrop={drop}>
          <ArrowUpload24Regular /><Text block weight="semibold">Déposez vos PDF ici ou cliquez pour les sélectionner</Text>
          <Text block size={200}>10 fichiers maximum, 50 Mo par fichier et 200 Mo au total</Text>
        </div><input ref={inputRef} className={styles.hidden} type="file" accept=".pdf,application/pdf" multiple disabled={locked} onChange={selectFiles} /></Field>
        {reading && <Spinner size="tiny" label="Lecture des PDF" />}
        {pdfs.length > 0 && <div className={styles.files}>{pdfs.map(pdf => <div className={styles.file} key={pdf.id}>
          <Text>{pdf.file.name} — {pdf.pageCount} page{pdf.pageCount > 1 ? 's' : ''} — {formatFileSize(pdf.file.size)}</Text>
          {!locked && <Button appearance="subtle" onClick={() => { setPdfs(current => current.filter(item => item.id !== pdf.id)); attempt.current = null; }}>Retirer</Button>}
        </div>)}</div>}
        {fileErrors.map(message => <Text className={styles.error} key={message}>{message}</Text>)}
        <div className={styles.estimate} aria-label="Estimation tarifaire"><Text>Tarif</Text><Text>0,27 € HT / page</Text>
          <Text>Calcul</Text><Text>{serverPages} × 0,27 €</Text>
          {serverAmount > serverPages * additionalDocumentsPricePerPageCents && <><Text>Minimum par demande</Text><Text>{formatCents(additionalDocumentsMinimumAmountCents)} HT</Text></>}
          <Text weight="semibold">Total</Text><Text weight="semibold">{formatCents(serverAmount)} HT</Text></div>
        {stage && stage.totalPages !== localPages && <Text>Le serveur a vérifié {stage.totalPages} pages ; ce total remplace l’estimation locale.</Text>}
        </>}
        {phase === 'awaiting-return' && <Text role="status">Finalisez l’autorisation dans la fenêtre Stripe.</Text>}
        {phase === 'verifying' && <Spinner size="tiny" label="Vérification de votre autorisation en cours…" />}
        {phase === 'pending' && <Text role="status">Autorisation en cours de vérification. Vous pouvez la vérifier à nouveau sans renvoyer les PDF.</Text>}
        {error && <Text className={styles.error} role="alert">{error}</Text>}
      </>}
    </DialogContent><DialogActions className={styles.actions}>
      {phase === 'pending' && (stage || recoveredRequestId) && <Button appearance="secondary" onClick={() => void checkAuthorization(false)}>Vérifier l’autorisation</Button>}
      {phase === 'pending' && (stage || recoveredRequestId) && <Button appearance="secondary" onClick={() => void cancelRequest()}>Annuler la demande</Button>}
      {phase !== 'authorized' && phase !== 'awaiting-return' && phase !== 'verifying' && <Button appearance="primary" disabled={reading || busy.current}
        onClick={() => void continueToPayment()}>{(stage || recoveredRequestId) && phase === 'pending' ? 'Rouvrir le paiement' : 'Continuer vers le paiement'}</Button>}
    </DialogActions></DialogBody>
  </DialogSurface></Dialog>;
}
