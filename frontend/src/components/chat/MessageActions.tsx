import { memo, useState, useCallback } from 'react';
import { Tooltip } from '@fluentui/react-components';
import {
  CopyRegular,
  ArrowClockwiseRegular,
} from '@fluentui/react-icons';
import styles from './MessageActions.module.css';

interface MessageActionsProps {
  content: string;
  onRegenerate: () => void;
}

function MessageActionsComponent({ content, onRegenerate }: MessageActionsProps) {
  const [copied, setCopied] = useState(false);

  const handleCopy = useCallback(async () => {
    try {
      await navigator.clipboard.writeText(content);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch (err) {
      console.warn('Clipboard copy failed:', err);
    }
  }, [content]);

  return (
    <div className={styles.actionsBar}>
      <Tooltip content={copied ? 'Copié !' : 'Copier'} relationship="label" withArrow>
        <button
          className={styles.actionButton}
          onClick={handleCopy}
          aria-label="Copier le message"
        >
          {copied && <span className={styles.copiedTooltip} role="status" aria-live="polite">Copié !</span>}
          <CopyRegular fontSize={16} />
        </button>
      </Tooltip>

      <Tooltip content="Régénérer" relationship="label" withArrow>
        <button
          className={styles.actionButton}
          onClick={onRegenerate}
          aria-label="Régénérer la réponse"
        >
          <ArrowClockwiseRegular fontSize={16} />
        </button>
      </Tooltip>

    </div>
  );
}

export const MessageActions = memo(MessageActionsComponent);
