import { useEffect, useMemo, useRef, useState, type ChangeEvent, type DragEvent } from 'react';
import { Button, Dialog, DialogActions, DialogBody, DialogContent, DialogSurface, DialogTitle, Field, Input, Spinner, Text, Textarea, makeStyles, tokens } from '@fluentui/react-components';
import { ArrowUpload24Regular } from '@fluentui/react-icons';
import { calculateMaximumAuthorizationPrice, calculatePreparationPrice, MAXIMUM_FIRST_SUBSCRIPTION_CENTS } from '../../pages/start/documentPricing';
import { authorizationConfirmedMessage, checkoutReturnedMessage, checkoutWindowName, isTrustedCheckoutMessage } from '../../pages/start/checkoutPopup';
import { formatFileSize, inspectPdfFiles, type SelectedPdf } from '../../pages/start/pdfSelection';
import { createCompanyMachineRequestPayment, submitCompanyMachineRequest, waitForCompanyMachineRequestAuthorization } from '../../services/companyMachineRequestPaymentApi';

const useStyles = makeStyles({
  content: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, minWidth: 'min(560px, 80vw)' },
  fields: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: tokens.spacingHorizontalM },
  full: { gridColumn: '1 / -1' },
  dropzone: { padding: tokens.spacingVerticalL, border: `1px dashed ${tokens.colorBrandStroke1}`, borderRadius: tokens.borderRadiusMedium, textAlign: 'center', cursor: 'pointer' },
  hidden: { display: 'none' },
  files: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  file: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: tokens.spacingHorizontalM },
  error: { color: tokens.colorPaletteRedForeground1 },
  estimate: { display: 'grid', gridTemplateColumns: '1fr auto', gap: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalL}` },
  authorized: { padding: tokens.spacingVerticalL, borderRadius: tokens.borderRadiusMedium, backgroundColor: tokens.colorPaletteGreenBackground1 },
});

type MachineValues = { machineName: string; manufacturer: string; model: string; serialNumber: string; description: string };
const initialValues: MachineValues = { machineName: '', manufacturer: '', model: '', serialNumber: '', description: '' };
type Attempt = { key: string; paymentRequestId?: string; authorized: boolean };
const formatCents = (cents: number) => new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'EUR' }).format(cents / 100);

export function AdditionalMachineRequestDialog({ open, onOpenChange, getAccessToken }: { open: boolean; onOpenChange: (open: boolean) => void; getAccessToken: () => Promise<string | null> }) {
  const styles = useStyles();
  const inputRef = useRef<HTMLInputElement>(null);
  const nextFileId = useRef(0);
  const attempt = useRef<Attempt | null>(null);
  const popup = useRef<Window | null>(null);
  const lock = useRef(false);
  const [values, setValues] = useState(initialValues);
  const [pdfs, setPdfs] = useState<SelectedPdf[]>([]);
  const [fileErrors, setFileErrors] = useState<string[]>([]);
  const [reading, setReading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [authorized, setAuthorized] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [uploadFailed, setUploadFailed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const totalPages = useMemo(() => pdfs.reduce((sum, pdf) => sum + pdf.pageCount, 0), [pdfs]);
  const preparation = useMemo(() => calculatePreparationPrice(totalPages), [totalPages]);
  const maximum = useMemo(() => calculateMaximumAuthorizationPrice(totalPages), [totalPages]);

  useEffect(() => {
    const receive = (event: MessageEvent) => {
      if (!isTrustedCheckoutMessage(event, popup.current, checkoutReturnedMessage)) return;
      if (attempt.current?.authorized) popup.current?.postMessage({ type: authorizationConfirmedMessage }, window.location.origin);
    };
    window.addEventListener('message', receive);
    return () => window.removeEventListener('message', receive);
  }, []);

  const addFiles = async (files: File[]) => {
    if (!files.length || lock.current || authorized) return;
    setReading(true);
    const result = await inspectPdfFiles(files, pdfs, () => nextFileId.current++);
    setPdfs(current => [...current, ...result.accepted]);
    setFileErrors(result.errors);
    setReading(false);
  };

  const uploadRequest = async (machine: MachineValues = values) => {
    if (!attempt.current?.authorized || !attempt.current.paymentRequestId) return;
    setSubmitting(true);
    setUploadFailed(false);
    setError(null);
    try {
      await submitCompanyMachineRequest(getAccessToken, attempt.current.paymentRequestId, machine, pdfs.map(pdf => pdf.file));
      setSubmitted(true);
      window.focus();
      popup.current?.close();
    } catch (caught) {
      setUploadFailed(true);
      setError(caught instanceof Error ? caught.message : 'L’envoi de la demande a échoué.');
      throw caught;
    } finally {
      setSubmitting(false);
    }
  };

  const submit = async () => {
    if (lock.current || authorized) return;
    const trimmed = Object.fromEntries(Object.entries(values).map(([key, value]) => [key, value.trim()])) as MachineValues;
    setValues(trimmed);
    if (!trimmed.machineName || !trimmed.manufacturer || !trimmed.model || pdfs.length === 0 || fileErrors.length || reading) {
      setError('Le nom, le fabricant, le modèle et au moins un PDF valide sont obligatoires.');
      return;
    }
    lock.current = true;
    setSubmitting(true);
    setError(null);
    try {
      if (!attempt.current) attempt.current = { key: crypto.randomUUID(), authorized: false };
      if (!attempt.current.paymentRequestId) {
        popup.current = window.open('', checkoutWindowName);
        if (!popup.current) throw new Error('Autorisez l’ouverture de la fenêtre Stripe pour poursuivre.');
        const payment = await createCompanyMachineRequestPayment(getAccessToken, pdfs.map(pdf => pdf.file), attempt.current.key);
        attempt.current.paymentRequestId = payment.paymentRequestId;
        if (!payment.checkoutUrl) throw new Error('La page de paiement Stripe est indisponible.');
        popup.current.location.href = payment.checkoutUrl;
      } else if (!popup.current || popup.current.closed) {
        popup.current = window.open('', checkoutWindowName);
      }
      await waitForCompanyMachineRequestAuthorization(getAccessToken, attempt.current.paymentRequestId);
      attempt.current.authorized = true;
      setAuthorized(true);
      popup.current?.postMessage({ type: authorizationConfirmedMessage }, window.location.origin);
      await uploadRequest(trimmed);
    } catch (caught) {
      if (attempt.current?.authorized) setUploadFailed(true);
      setError(caught instanceof Error ? caught.message : 'La demande n’a pas pu être préparée.');
    } finally {
      lock.current = false;
      setSubmitting(false);
    }
  };

  const field = (name: keyof MachineValues) => ({ value: values[name], disabled: submitting || authorized, onChange: (_event: unknown, data: { value: string }) => setValues(current => ({ ...current, [name]: data.value })) });
  const selectFiles = (event: ChangeEvent<HTMLInputElement>) => { void addFiles(Array.from(event.target.files ?? [])); event.target.value = ''; };
  const drop = (event: DragEvent<HTMLDivElement>) => { event.preventDefault(); void addFiles(Array.from(event.dataTransfer.files)); };

  return <Dialog open={open} onOpenChange={(_event, data) => !submitting && onOpenChange(data.open)}><DialogSurface>
    <DialogTitle>Demander l’ajout d’une machine</DialogTitle><DialogBody><DialogContent className={styles.content}>
      {submitted ? <div className={styles.authorized} role="status"><Text weight="semibold" size={500}>Demande envoyée</Text><Text block>Votre demande d’ajout de machine a été transmise. Elle sera vérifiée avant activation.</Text></div> : authorized ? <div className={styles.authorized} role="status"><Text weight="semibold" size={500}>{uploadFailed ? 'Paiement autorisé — l’envoi de la demande a échoué.' : 'Envoi de votre demande…'}</Text>{error && <Text block className={styles.error}>{error}</Text>}{uploadFailed && <Button appearance="primary" disabled={submitting} onClick={() => void uploadRequest().catch(() => undefined)}>Réessayer l’envoi</Button>}</div> : <>
        <div className={styles.fields}>
          <Field label="Nom de la machine" required><Input {...field('machineName')} /></Field>
          <Field label="Fabricant ou marque" required><Input {...field('manufacturer')} /></Field>
          <Field label="Modèle" required><Input {...field('model')} /></Field>
          <Field label="Numéro de série ou référence"><Input {...field('serialNumber')} /></Field>
          <Field className={styles.full} label="Description"><Textarea {...field('description')} /></Field>
        </div>
        <Field label="Documents PDF" required><div className={styles.dropzone} role="button" tabIndex={0} onClick={() => inputRef.current?.click()} onKeyDown={event => event.key === 'Enter' && inputRef.current?.click()} onDragOver={event => event.preventDefault()} onDrop={drop}>
          <ArrowUpload24Regular /><Text block weight="semibold">Déposez vos PDF ici ou cliquez pour les sélectionner</Text><Text block size={200}>10 fichiers maximum, 50 Mo par fichier et 200 Mo au total</Text>
        </div><input ref={inputRef} className={styles.hidden} type="file" accept=".pdf,application/pdf" multiple onChange={selectFiles} /></Field>
        {reading && <Spinner size="tiny" label="Lecture des PDF" />}
        {pdfs.length > 0 && <div className={styles.files}>{pdfs.map(pdf => <div className={styles.file} key={pdf.id}><Text>{pdf.file.name} — {pdf.pageCount} page{pdf.pageCount > 1 ? 's' : ''} — {formatFileSize(pdf.file.size)}</Text><Button appearance="subtle" onClick={() => setPdfs(current => current.filter(item => item.id !== pdf.id))}>Retirer</Button></div>)}</div>}
        {fileErrors.map(message => <Text className={styles.error} key={message}>{message}</Text>)}
        <div className={styles.estimate} aria-label="Estimation tarifaire"><Text>Préparation documentaire ({totalPages} pages)</Text><Text weight="semibold">{formatCents(preparation.totalPriceCents)} HT</Text><Text>Abonnement mensuel maximal autorisé</Text><Text weight="semibold">{formatCents(MAXIMUM_FIRST_SUBSCRIPTION_CENTS)} HT</Text><Text weight="semibold">Autorisation maximale totale</Text><Text weight="semibold">{formatCents(maximum)} HT</Text></div>
        <Text size={200}>L’abonnement comprend 10,00 € de crédit IA et 19,90 € de service. Lors de l’acceptation ultérieure, le service sera calculé au prorata ; aucun prorata n’est simulé ici.</Text>
        {error && <Text className={styles.error} role="alert">{error}</Text>}
      </>}
    </DialogContent><DialogActions><Button appearance="secondary" disabled={submitting} onClick={() => onOpenChange(false)}>Fermer</Button>{!authorized && <Button appearance="primary" disabled={submitting || reading} onClick={submit}>{submitting ? <Spinner size="tiny" /> : `Autoriser ${formatCents(maximum)}`}</Button>}</DialogActions></DialogBody>
  </DialogSurface></Dialog>;
}
