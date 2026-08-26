import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
} from '@fluentui/react-components';
import styles from './KeyboardShortcuts.module.css';

interface KeyboardShortcutsProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

interface Shortcut {
  keys: string[][];
  description: string;
}

const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.userAgent);

const shortcuts: Shortcut[] = [
  { keys: [[isMac ? '⌘' : 'Ctrl', 'N']], description: 'Nouvelle conversation' },
  { keys: [['Escape']], description: 'Annuler la génération' },
  { keys: [['Enter']], description: 'Envoyer le message' },
  { keys: [['Shift', 'Enter']], description: 'Nouvelle ligne' },
];

export const KeyboardShortcuts: React.FC<KeyboardShortcutsProps> = ({ open, onOpenChange }) => {
  return (
    <Dialog open={open} onOpenChange={(_e, data) => onOpenChange(data.open)}>
      <DialogSurface className={styles.backdrop}>
        <DialogTitle className={styles.title}>Raccourcis clavier</DialogTitle>
        <DialogBody>
          <DialogContent>
            <div className={styles.grid}>
              {shortcuts.map((shortcut) => (
                <div key={shortcut.description} className={styles.row}>
                  <div className={styles.keys}>
                    {shortcut.keys.map((combo, ci) => (
                      <span key={ci}>
                        {ci > 0 && <span className={styles.separator}> / </span>}
                        {combo.map((key, ki) => (
                          <span key={ki}>
                            {ki > 0 && <span className={styles.separator}>+</span>}
                            <kbd className={styles.kbd}>{key}</kbd>
                          </span>
                        ))}
                      </span>
                    ))}
                  </div>
                  <span className={styles.description}>{shortcut.description}</span>
                </div>
              ))}
            </div>
          </DialogContent>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};
