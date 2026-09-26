import { useEffect, useMemo, useRef, useState, type ChangeEvent, type DragEvent, type FormEvent } from 'react';
import { ArrowUpload24Regular } from '@fluentui/react-icons';
import { PublicHeader } from '../../components/marketing/PublicHeader';
import { PublicFooter } from '../../components/marketing/ClosingSections';
import landingStyles from '../landing/LandingPage.module.css';
import styles from './StartPage.module.css';
import { calculateMaximumAuthorizationPrice, calculatePreparationPrice } from './documentPricing';
import { trimStartFormValues, validateStartForm, type RequiredStartField, type StartFormValues } from './startFormValidation';
import { MachineRequestSubmissionError, submitMachineRequest, type MachineRequestResponse } from '../../services/machineRequestApi';
import { createMachineRequestPayment, MachineRequestPaymentTerminalError, waitForMachineRequestAuthorization } from '../../services/machineRequestPaymentApi';
import { authorizationConfirmedMessage, checkoutReturnedMessage, checkoutWindowName, isCheckoutReturnWindow, isTrustedCheckoutMessage, requestSubmittedMessage } from './checkoutPopup';
import { formatFileSize, inspectPdfFiles, type SelectedPdf } from './pdfSelection';

const steps = ['Coordonnées', 'Machine', 'Documents', 'Récapitulatif'];
const euroFormatter = new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'EUR' });
const initialFormValues: StartFormValues = {
  firstName: '', lastName: '', company: '', email: '', phone: '',
  machineName: '', manufacturer: '', model: '', serialNumber: '', description: '',
};

type SubmissionPhase = 'idle' | 'preparing' | 'waiting' | 'uploading';

type PaymentAttempt = {
  idempotencyKey: string;
  totalPages: number;
  email: string;
  paymentRequestId?: string;
  checkoutUrl?: string;
  authorized: boolean;
};

const paymentAttemptStorageKey = 'diaglink:initial-machine-payment-attempt';

function readPaymentAttempt(): PaymentAttempt | null {
  try {
    const value = localStorage.getItem(paymentAttemptStorageKey);
    if (!value) return null;
    const attempt = JSON.parse(value) as Partial<PaymentAttempt>;
    return typeof attempt.idempotencyKey === 'string'
      && typeof attempt.totalPages === 'number'
      && typeof attempt.email === 'string'
      && typeof attempt.authorized === 'boolean'
      ? attempt as PaymentAttempt
      : null;
  } catch {
    return null;
  }
}

function writePaymentAttempt(attempt: PaymentAttempt) {
  localStorage.setItem(paymentAttemptStorageKey, JSON.stringify(attempt));
}

function formatCents(cents: number) {
  return euroFormatter.format(cents / 100);
}

export function StartPage({ isAuthenticated }: { isAuthenticated: boolean }) {
  const loginTarget = isAuthenticated ? '/app' : '/login';
  const inputRef = useRef<HTMLInputElement>(null);
  const nextFileId = useRef(0);
  const submissionLock = useRef(false);
  const paymentAttempt = useRef<PaymentAttempt | null>(null);
  const checkoutWindow = useRef<Window | null>(null);
  const isCheckoutReturn = useMemo(isCheckoutReturnWindow, []);
  const [returnAuthorizationConfirmed, setReturnAuthorizationConfirmed] = useState(false);
  const [checkoutReturnObserved, setCheckoutReturnObserved] = useState(false);
  const [selectedPdfs, setSelectedPdfs] = useState<SelectedPdf[]>([]);
  const [fileErrors, setFileErrors] = useState<string[]>([]);
  const [isReadingFiles, setIsReadingFiles] = useState(false);
  const [formValues, setFormValues] = useState<StartFormValues>(initialFormValues);
  const [touchedFields, setTouchedFields] = useState<Partial<Record<RequiredStartField, boolean>>>({});
  const [documentsTouched, setDocumentsTouched] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submissionPhase, setSubmissionPhase] = useState<SubmissionPhase>('idle');
  const [submissionError, setSubmissionError] = useState<string | null>(null);
  const [confirmation, setConfirmation] = useState<MachineRequestResponse | null>(null);
  const totalPages = useMemo(() => selectedPdfs.reduce((total, pdf) => total + pdf.pageCount, 0), [selectedPdfs]);
  // Estimation d'interface uniquement : pages et tarif devront obligatoirement être
  // recalculés et validés côté serveur avant toute facturation.
  const preparationPrice = useMemo(() => calculatePreparationPrice(totalPages), [totalPages]);
  const maximumAuthorizationPrice = useMemo(() => calculateMaximumAuthorizationPrice(totalPages), [totalPages]);
  const formErrors = useMemo(() => validateStartForm(formValues, selectedPdfs.length), [formValues, selectedPdfs.length]);
  const isFormComplete = Object.keys(formErrors).length === 0 && !isReadingFiles && fileErrors.length === 0 && !isSubmitting;

  useEffect(() => {
    if (isCheckoutReturn) {
      const opener = window.opener;
      if (!opener) return;

      opener.postMessage({ type: checkoutReturnedMessage }, window.location.origin);
      const handleOpenerMessage = (event: MessageEvent) => {
        if (event.origin !== window.location.origin || event.source !== opener || !event.data || typeof event.data !== 'object') return;
        if (event.data.type === authorizationConfirmedMessage) setReturnAuthorizationConfirmed(true);
        if (event.data.type === requestSubmittedMessage) window.close();
      };
      window.addEventListener('message', handleOpenerMessage);
      return () => window.removeEventListener('message', handleOpenerMessage);
    }

    const handleCheckoutMessage = (event: MessageEvent) => {
      const popup = checkoutWindow.current;
      if (!isTrustedCheckoutMessage(event, popup, checkoutReturnedMessage)) return;
      setCheckoutReturnObserved(true);
      if (paymentAttempt.current?.authorized) {
        popup!.postMessage({ type: authorizationConfirmedMessage }, window.location.origin);
      }
    };
    window.addEventListener('message', handleCheckoutMessage);
    return () => window.removeEventListener('message', handleCheckoutMessage);
  }, [isCheckoutReturn]);

  useEffect(() => {
    if (!isSubmitting) return;
    const protect = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', protect);
    return () => window.removeEventListener('beforeunload', protect);
  }, [isSubmitting]);

  const updateField = (name: keyof StartFormValues, value: string) => {
    setFormValues(current => ({ ...current, [name]: value }));
  };

  const finishFieldInteraction = (name: keyof StartFormValues) => {
    setFormValues(current => ({ ...current, [name]: current[name].trim() }));
    if (name in initialFormValues && !['serialNumber', 'description'].includes(name)) {
      setTouchedFields(current => ({ ...current, [name as RequiredStartField]: true }));
    }
  };

  const fieldProps = (name: keyof StartFormValues) => ({
    value: formValues[name],
    disabled: isSubmitting,
    onChange: (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => updateField(name, event.target.value),
    onBlur: () => finishFieldInteraction(name),
    'aria-invalid': Boolean(touchedFields[name as RequiredStartField] && formErrors[name as RequiredStartField]),
    'aria-describedby': touchedFields[name as RequiredStartField] && formErrors[name as RequiredStartField] ? `${name}-error` : undefined,
  });

  const fieldError = (name: RequiredStartField) => touchedFields[name] && formErrors[name]
    ? <small className={styles.fieldError} id={`${name}-error`}>{formErrors[name]}</small>
    : null;

  const addFiles = async (files: File[]) => {
    if (files.length === 0 || submissionLock.current) return;

    setIsReadingFiles(true);
    const { accepted: acceptedFiles, errors } = await inspectPdfFiles(files, selectedPdfs, () => nextFileId.current++);

    setSelectedPdfs(current => [...current, ...acceptedFiles]);
    setFileErrors(errors);
    setIsReadingFiles(false);
  };

  const handleFileSelection = (event: ChangeEvent<HTMLInputElement>) => {
    void addFiles(Array.from(event.target.files ?? []));
    event.target.value = '';
  };

  const handleDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    if (submissionLock.current) return;
    setDocumentsTouched(true);
    void addFiles(Array.from(event.dataTransfer.files));
  };

  const removePdf = (id: number) => {
    if (submissionLock.current) return;
    setSelectedPdfs(current => current.filter(pdf => pdf.id !== id));
  };

  const handleValidation = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (submissionLock.current) return;

    const trimmedValues = trimStartFormValues(formValues);
    const finalErrors = validateStartForm(trimmedValues, selectedPdfs.length);
    setFormValues(trimmedValues);
    setTouchedFields({ firstName: true, lastName: true, company: true, email: true, phone: true, machineName: true, manufacturer: true, model: true });
    setDocumentsTouched(true);
    if (Object.keys(finalErrors).length > 0 || isReadingFiles || fileErrors.length > 0) return;

    submissionLock.current = true;
    setIsSubmitting(true);
    setSubmissionError(null);
    try {
      const storedAttempt = readPaymentAttempt();
      paymentAttempt.current = storedAttempt?.totalPages === totalPages
        && storedAttempt.email.toLocaleLowerCase() === trimmedValues.email.toLocaleLowerCase()
        ? storedAttempt
        : {
            idempotencyKey: crypto.randomUUID(),
            totalPages,
            email: trimmedValues.email,
            authorized: false,
          };
      writePaymentAttempt(paymentAttempt.current);

      if (!paymentAttempt.current.paymentRequestId) {
        checkoutWindow.current = window.open('', checkoutWindowName);
        if (!checkoutWindow.current) throw new Error('Autorisez l’ouverture de la fenêtre Stripe pour poursuivre.');
        setSubmissionPhase('preparing');
        const payment = await createMachineRequestPayment(
          totalPages, trimmedValues.email, paymentAttempt.current.idempotencyKey);
        paymentAttempt.current = {
          ...paymentAttempt.current,
          paymentRequestId: payment.paymentRequestId,
          checkoutUrl: payment.checkoutUrl,
        };
        writePaymentAttempt(paymentAttempt.current);
        checkoutWindow.current.location.href = payment.checkoutUrl!;
      } else if (!paymentAttempt.current.authorized && paymentAttempt.current.checkoutUrl) {
        checkoutWindow.current = window.open(paymentAttempt.current.checkoutUrl, checkoutWindowName);
        if (!checkoutWindow.current) throw new Error('Autorisez l’ouverture de la fenêtre Stripe pour poursuivre.');
      }
      const paymentRequestId = paymentAttempt.current.paymentRequestId;
      if (!paymentRequestId) throw new Error('Le paiement n’a pas pu être préparé.');
      if (!paymentAttempt.current.authorized) {
        setSubmissionPhase('waiting');
        await waitForMachineRequestAuthorization(paymentRequestId);
        paymentAttempt.current.authorized = true;
        writePaymentAttempt(paymentAttempt.current);
        checkoutWindow.current?.postMessage({ type: authorizationConfirmedMessage }, window.location.origin);
      }
      setSubmissionPhase('uploading');
      const completedRequest = await submitMachineRequest(trimmedValues, selectedPdfs.map(pdf => pdf.file), paymentRequestId);
      setConfirmation(completedRequest);
      localStorage.removeItem(paymentAttemptStorageKey);
      paymentAttempt.current = null;
      window.focus();
      checkoutWindow.current?.postMessage({ type: requestSubmittedMessage }, window.location.origin);
      checkoutWindow.current?.close();
    } catch (error) {
      if (!paymentAttempt.current?.paymentRequestId) checkoutWindow.current?.close();
      if (error instanceof MachineRequestPaymentTerminalError) {
        localStorage.removeItem(paymentAttemptStorageKey);
        paymentAttempt.current = null;
      }
      setSubmissionError(error instanceof MachineRequestSubmissionError
        ? error.message
        : error instanceof Error ? error.message
        : 'La demande n’a pas pu être envoyée. Vérifiez les informations et réessayez.');
    } finally {
      submissionLock.current = false;
      setIsSubmitting(false);
      setSubmissionPhase('idle');
    }
    // Validation locale uniquement. La future soumission HTTP partira de formValues et selectedPdfs,
    // après nouvelle validation et recalcul obligatoires côté serveur.
  };

  return (
    <div className={landingStyles.page}>
      <PublicHeader loginTarget={loginTarget} isAuthenticated={isAuthenticated} landingPath="/" />
      <main className={styles.main} id="top">
        <section className={styles.hero}>
          <div className={landingStyles.container}>
            <p className={landingStyles.eyebrow}>Ajouter une machine</p>
            <h1>Configurez votre première machine.</h1>
            <p>Renseignez votre machine et transmettez sa documentation technique. Nous analyserons les documents avant de vous proposer le coût de préparation.</p>
            <ol className={styles.progress} aria-label="Étapes de configuration">
              {steps.map((step, index) => <li key={step}><span>{index + 1}</span><strong>{step}</strong></li>)}
            </ol>
          </div>
        </section>

        {isCheckoutReturn ? (
          <section className={`${landingStyles.container} ${styles.content} ${styles.confirmation}`} aria-labelledby="checkout-return-title">
            <div className={`${styles.card} ${styles.confirmationCard}`}>
              <p className={styles.confirmationEyebrow}>Retour de Stripe</p>
              <h2 id="checkout-return-title">{returnAuthorizationConfirmed
                ? 'Paiement autorisé — finalisation de votre demande…'
                : 'Vérification de votre autorisation en cours…'}</h2>
              <p>{window.opener
                ? 'Votre demande est finalisée dans l’onglet d’origine. Cette fenêtre se fermera automatiquement une fois la transmission confirmée.'
                : 'Revenez à l’onglet d’origine pour suivre la finalisation de votre demande. Vous pouvez fermer cette fenêtre.'}</p>
            </div>
          </section>
        ) : confirmation ? (
          <section className={`${landingStyles.container} ${styles.content} ${styles.confirmation}`} aria-labelledby="confirmation-title">
            <div className={`${styles.card} ${styles.confirmationCard}`}>
              <p className={styles.confirmationEyebrow}>Demande transmise</p>
              <h2 id="confirmation-title">Votre demande a bien été transmise.</h2>
              <p>Nous allons vérifier votre documentation technique avant la mise en place de votre machine DiagLink.</p>
              <dl className={styles.confirmationDetails}>
                <div><dt>Référence de la demande</dt><dd>{confirmation.requestId}</dd></div>
                <div><dt>Documents transmis</dt><dd>{confirmation.documentCount}</dd></div>
                <div><dt>Pages vérifiées</dt><dd>{confirmation.totalPages}</dd></div>
                <div><dt>Préparation documentaire</dt><dd>{euroFormatter.format(confirmation.preparationTotal)} HT</dd></div>
                {confirmation.additionalPages > 0 && <div><dt>Pages supplémentaires</dt><dd>{confirmation.additionalPages}</dd></div>}
              </dl>
              <p className={styles.noPayment}>Le montant a été autorisé, mais n’a pas encore été encaissé. Vos documents vont maintenant être vérifiés.</p>
              <p>Nous vous contacterons après vérification de votre demande.</p>
            </div>
          </section>
        ) : <form className={`${landingStyles.container} ${styles.content}`} noValidate onSubmit={handleValidation}>
          <section className={styles.card}>
            <div className={styles.cardHeading}><h2>Vos coordonnées</h2><p>Ces informations nous permettront de créer et de suivre votre demande.</p></div>
            <div className={styles.fields}>
              <label>Prénom<input type="text" name="firstName" autoComplete="given-name" {...fieldProps('firstName')} />{fieldError('firstName')}</label>
              <label>Nom<input type="text" name="lastName" autoComplete="family-name" {...fieldProps('lastName')} />{fieldError('lastName')}</label>
              <label>Entreprise<input type="text" name="company" autoComplete="organization" {...fieldProps('company')} />{fieldError('company')}</label>
              <label>Adresse e-mail<input type="email" name="email" autoComplete="email" {...fieldProps('email')} />{fieldError('email')}</label>
              <label>Téléphone<input type="tel" name="phone" autoComplete="tel" {...fieldProps('phone')} />{fieldError('phone')}</label>
            </div>
          </section>

          <section className={styles.card}>
            <div className={styles.cardHeading}><h2>Votre machine</h2><p>Identifiez la machine pour laquelle vous souhaitez préparer DiagLink.</p></div>
            <div className={styles.fields}>
              <label>Nom de la machine<input type="text" name="machineName" {...fieldProps('machineName')} />{fieldError('machineName')}</label>
              <label>Fabricant / marque<input type="text" name="manufacturer" {...fieldProps('manufacturer')} />{fieldError('manufacturer')}</label>
              <label>Modèle<input type="text" name="model" {...fieldProps('model')} />{fieldError('model')}</label>
              <label>Référence ou numéro de série <span>(facultatif)</span><input type="text" name="serialNumber" {...fieldProps('serialNumber')} /></label>
              <label className={styles.fullField}>Description / informations complémentaires<textarea name="description" rows={5} {...fieldProps('description')} /></label>
            </div>
          </section>

          <section className={styles.card}>
            <div className={styles.cardHeading}><h2>Documentation technique</h2><p>Ajoutez les documents qui permettront à DiagLink de préparer la base documentaire de votre machine.</p></div>
            <div className={styles.dropzone} aria-disabled={isSubmitting} onClick={() => { if (!isSubmitting) { setDocumentsTouched(true); inputRef.current?.click(); } }} onDragOver={event => event.preventDefault()} onDrop={handleDrop} role="button" tabIndex={0} onKeyDown={event => { if (!isSubmitting && (event.key === 'Enter' || event.key === ' ')) { setDocumentsTouched(true); inputRef.current?.click(); } }}>
              <input ref={inputRef} className={styles.fileInput} type="file" accept="application/pdf,.pdf" multiple disabled={isSubmitting} onChange={handleFileSelection} />
              <span className={styles.uploadIcon}><ArrowUpload24Regular /></span>
              <strong>Déposez vos documents ici</strong>
              <span>ou sélectionnez des fichiers</span>
              <small>PDF</small>
            </div>
            {isReadingFiles && <p className={styles.readingStatus}>Analyse des PDF en cours…</p>}
            {fileErrors.length > 0 && <ul className={styles.fileErrors} role="alert">{fileErrors.map((error, index) => <li key={`${error}-${index}`}>{error}</li>)}</ul>}
            {documentsTouched && formErrors.documents && !isReadingFiles && <p className={styles.documentError}>{formErrors.documents}</p>}
            {selectedPdfs.length > 0 && (
              <ul className={styles.fileList} aria-label="Documents PDF sélectionnés">
                {selectedPdfs.map(pdf => (
                  <li key={pdf.id}>
                    <span><strong>{pdf.file.name}</strong><small>{pdf.pageCount} {pdf.pageCount === 1 ? 'page' : 'pages'} · {formatFileSize(pdf.file.size)}</small></span>
                    <button type="button" disabled={isSubmitting} onClick={() => removePdf(pdf.id)} aria-label={`Retirer ${pdf.file.name}`}>Retirer</button>
                  </li>
                ))}
              </ul>
            )}
            <p className={styles.documentHint}>Manuels techniques, schémas électriques, notices, vues éclatées, catalogues de pièces…</p>
          </section>

          <section className={`${styles.card} ${styles.estimateCard}`}>
            <div className={styles.cardHeading}><h2>Récapitulatif tarifaire</h2></div>
            <dl className={styles.estimateLines}>
              <div><dt>1er abonnement DiagLink</dt><dd>Jusqu’à 29,90 € HT</dd></div>
              {totalPages === 0 ? (
                <>
                  <div><dt>Préparation documentaire</dt><dd>99,90 € HT<small>Jusqu’à 400 pages incluses</small></dd></div>
                  <div><dt>Pages supplémentaires</dt><dd>0,27 € HT / page au-delà de 400 pages</dd></div>
                </>
              ) : (
                <>
                  <div><dt>Documentation transmise</dt><dd>{totalPages} {totalPages === 1 ? 'page' : 'pages'}</dd></div>
                  <div><dt>Préparation documentaire</dt><dd>99,90 € HT<small>{totalPages <= 400 ? 'Jusqu’à 400 pages incluses' : '400 pages incluses'}</small></dd></div>
                  {preparationPrice.extraPages > 0 && <div><dt>Pages supplémentaires</dt><dd>{preparationPrice.extraPages} × 0,27 € HT<small>{formatCents(preparationPrice.extraPriceCents)} HT</small></dd></div>}
                  <div className={styles.totalLine}><dt>Total préparation documentaire</dt><dd>{formatCents(preparationPrice.totalPriceCents)} HT<small>Coût unique</small></dd></div>
                </>
              )}
              <div className={styles.totalLine}><dt>Autorisation maximale</dt><dd>{formatCents(maximumAuthorizationPrice)} HT</dd></div>
            </dl>
            <p className={styles.calculationNote}>Le montant exact de la préparation sera calculé selon le nombre total de pages des documents transmis.</p>
            <p className={styles.noPayment}>Le premier abonnement sera ajusté selon la date d’activation. Après validation de vos documents, seul le montant réellement dû sera encaissé.</p>
          </section>

          <div className={styles.submitArea}>
            <button type="submit" disabled={!isFormComplete}>{isSubmitting
              ? submissionPhase === 'preparing' ? 'Préparation du paiement…'
                : submissionPhase === 'waiting' ? 'Autorisation en attente…'
                  : 'Transmission de vos documents…'
              : isFormComplete
                ? `Autoriser ${formatCents(maximumAuthorizationPrice)} et transmettre ma demande`
                : 'Ajouter vos documents pour continuer'}</button>
            {isSubmitting && <p className={styles.submissionStatus}>{submissionPhase === 'preparing'
              ? 'Préparation du paiement…'
              : submissionPhase === 'waiting' ? checkoutReturnObserved
                ? 'Retour de Stripe reçu. Vérification de l’autorisation en cours…'
                : 'Autorisation du paiement en attente…'
                : 'Paiement autorisé. Transmission de vos documents…'}</p>}
            {submissionError && <p className={styles.submissionError} role="alert">{submissionError}</p>}
            <p>La documentation sera vérifiée avant l’activation de votre abonnement.</p>
          </div>
        </form>}
      </main>
      <PublicFooter loginTarget={loginTarget} />
    </div>
  );
}
