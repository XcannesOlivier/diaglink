import { ArrowUploadRegular } from '@fluentui/react-icons';
import styles from './DropZone.module.css';

interface DropZoneProps {
  visible: boolean;
}

export const DropZone: React.FC<DropZoneProps> = ({ visible }) => {
  if (!visible) return null;

  return (
    <div className={styles.overlay} role="status" aria-live="polite">
      <div className={styles.dropArea}>
        <ArrowUploadRegular className={styles.icon} aria-hidden="true" />
        <span className={styles.label}>Déposez les fichiers ici</span>
        <span className={styles.hint}>Images, PDF et fichiers texte pris en charge</span>
      </div>
    </div>
  );
};
