import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
} from '@fluentui/react-components';
import { DialogCloseButton } from '../../components/core/DialogCloseButton';
import { ConditionsContent } from '../conditions/ConditionsPage';
import legalStyles from '../privacy/PrivacyPage.module.css';
import styles from './ConditionsDialog.module.css';

export function ConditionsDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <Dialog open={open} onOpenChange={(_event, data) => !data.open && onClose()}>
      <DialogSurface className={styles.surface} aria-labelledby="conditions-dialog-title">
        <DialogBody className={styles.body}>
          <DialogTitle
            id="conditions-dialog-title"
            action={<DialogCloseButton onClick={onClose} />}
          >
            <span className={styles.titleBlock}>
              <span>Conditions générales de service</span>
              <small>Clients professionnels</small>
            </span>
          </DialogTitle>
          <DialogContent className={styles.content}>
            <article className={`${legalStyles.article} ${styles.article}`}>
              <ConditionsContent compact showTitle={false} />
            </article>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onClose}>Fermer</Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
