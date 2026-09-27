import { useRef, useState, type FormEvent } from 'react';
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Spinner,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { submitSupportContact } from '../../services/supportContactApi';

const useStyles = makeStyles({
  content: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    minWidth: 'min(520px, 80vw)',
  },
  help: { color: tokens.colorNeutralForeground2 },
  error: { color: tokens.colorPaletteRedForeground1 },
  success: { color: tokens.colorPaletteGreenForeground1 },
});

type SubmissionStatus = 'idle' | 'submitting' | 'success' | 'error';

interface SupportContactDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  getAccessToken: () => Promise<string | null>;
  machineId?: string;
  onDiagLinkSessionExpired?: () => void;
}

export function SupportContactDialog({
  open,
  onOpenChange,
  getAccessToken,
  machineId,
  onDiagLinkSessionExpired,
}: SupportContactDialogProps) {
  const styles = useStyles();
  const submissionLock = useRef(false);
  const [message, setMessage] = useState('');
  const [status, setStatus] = useState<SubmissionStatus>('idle');
  const [feedback, setFeedback] = useState<string | null>(null);

  const close = () => {
    if (submissionLock.current) return;
    setStatus('idle');
    setFeedback(null);
    onOpenChange(false);
  };

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (submissionLock.current) return;

    const trimmedMessage = message.trim();
    if (!trimmedMessage) {
      setStatus('error');
      setFeedback('Le message est obligatoire.');
      return;
    }

    submissionLock.current = true;
    setStatus('submitting');
    setFeedback(null);
    try {
      const result = await submitSupportContact(getAccessToken, {
        message: trimmedMessage,
        ...(machineId ? { machineId } : {}),
      });

      if (result.kind === 'success' && result.data.success) {
        setMessage('');
        setStatus('success');
        setFeedback('Votre message a bien été envoyé.');
        return;
      }

      if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) {
        onDiagLinkSessionExpired?.();
      }

      setStatus('error');
      if (result.kind === 'validation-error') setFeedback(result.message);
      else if (result.kind === 'not-found') setFeedback('La machine sélectionnée n’est plus accessible.');
      else if (result.kind === 'forbidden') setFeedback('Vous n’êtes pas autorisé à contacter le support depuis ce compte.');
      else if (result.kind === 'unauthorized') setFeedback('Votre session a expiré. Veuillez vous reconnecter.');
      else setFeedback('Le message n’a pas pu être envoyé. Veuillez réessayer plus tard.');
    } finally {
      submissionLock.current = false;
    }
  };

  return (
    <Dialog open={open} onOpenChange={(_event, data) => !submissionLock.current && (data.open ? onOpenChange(true) : close())}>
      <DialogSurface>
        <form onSubmit={submit}>
          <DialogBody>
            <DialogTitle>Contacter DiagLink</DialogTitle>
            <DialogContent className={styles.content}>
              <Text className={styles.help}>
                Décrivez votre demande. Notre équipe vous répondra directement par e-mail.
              </Text>
              <Field label="Message" required>
                <Textarea
                  name="message"
                  rows={6}
                  maxLength={5000}
                  value={message}
                  disabled={status === 'submitting'}
                  onChange={(_event, data) => {
                    setMessage(data.value);
                    if (status !== 'submitting') {
                      setStatus('idle');
                      setFeedback(null);
                    }
                  }}
                />
              </Field>
              {feedback && (
                <Text
                  className={status === 'success' ? styles.success : styles.error}
                  role={status === 'error' ? 'alert' : 'status'}
                >
                  {feedback}
                </Text>
              )}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" type="button" disabled={status === 'submitting'} onClick={close}>
                Annuler
              </Button>
              <Button appearance="primary" type="submit" disabled={status === 'submitting'}>
                {status === 'submitting' ? <><Spinner size="tiny" /> Envoi…</> : 'Envoyer'}
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  );
}