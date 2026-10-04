import { Button } from '@fluentui/react-components';
import { Dismiss24Regular } from '@fluentui/react-icons';

interface DialogCloseButtonProps {
  disabled?: boolean;
  onClick: () => void;
}

export function DialogCloseButton({ disabled = false, onClick }: DialogCloseButtonProps) {
  return (
    <Button
      type="button"
      appearance="subtle"
      icon={<Dismiss24Regular />}
      aria-label="Fermer"
      data-dialog-close-button
      disabled={disabled}
      onClick={onClick}
    />
  );
}
