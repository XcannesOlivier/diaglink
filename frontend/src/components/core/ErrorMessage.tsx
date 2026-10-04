import { makeStyles, mergeClasses, tokens, Button, Text, MessageBar, MessageBarBody, MessageBarActions } from '@fluentui/react-components';
import { DismissRegular, ArrowClockwiseRegular } from '@fluentui/react-icons';
import { useThemeContext } from '../../contexts/ThemeContext';

const useStyles = makeStyles({
  container: {
    marginBottom: tokens.spacingVerticalM,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow4,
    backgroundColor: tokens.colorPaletteRedBackground2,
    borderLeftWidth: '4px',
    borderLeftStyle: 'solid',
    borderLeftColor: tokens.colorPaletteRedBorder2,
  },
  warningLight: {
    backgroundColor: '#FFF4E5',
    borderLeftColor: '#F59E0B',
  },
  warningDark: {
    backgroundColor: '#33280F',
    borderLeftColor: '#D97706',
  },
  messageText: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
  },
  warningTextLight: {
    color: '#7C4A03',
  },
  warningTextDark: {
    color: '#FDE68A',
  },
  actions: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
  },
});

export interface ErrorMessageProps {
  /**
   * Error message to display
   */
  message: string;

  /** Visual severity of the message. */
  intent?: 'error' | 'warning';
  
  /**
   * Whether the error is recoverable (shows retry button)
   */
  recoverable?: boolean;
  
  /**
   * Optional retry handler
   */
  onRetry?: () => void;
  
  /**
   * Optional dismiss handler
   */
  onDismiss?: () => void;
  
  /**
   * Optional custom action button
   */
  customAction?: {
    label: string;
    handler: () => void;
  };
}

/**
 * ErrorMessage component displays error messages with optional recovery actions
 * Used throughout the app for consistent error presentation
 */
export function ErrorMessage({ 
  message, 
  intent = 'error',
  recoverable = false, 
  onRetry, 
  onDismiss,
  customAction 
}: ErrorMessageProps) {
  const styles = useStyles();
  const { isDarkMode } = useThemeContext();
  const isWarning = intent === 'warning';
  
  // Defensive: ensure message is always a string
  const displayMessage = typeof message === 'string' 
    ? message 
    : 'Une erreur est survenue. Veuillez réessayer.';

  return (
    <MessageBar
      intent={intent}
      className={mergeClasses(
        styles.container,
        isWarning && (isDarkMode ? styles.warningDark : styles.warningLight),
      )}
      data-intent={intent}
      role="alert"
      aria-live="assertive"
      aria-atomic="true"
    >
      <MessageBarBody>
        <Text className={mergeClasses(
          styles.messageText,
          isWarning && (isDarkMode ? styles.warningTextDark : styles.warningTextLight),
        )}>{displayMessage}</Text>
      </MessageBarBody>
      <MessageBarActions
        containerAction={
          onDismiss ? (
            <Button
              onClick={onDismiss}
              appearance="transparent"
              icon={<DismissRegular />}
              size="small"
              aria-label="Ignorer l'erreur"
            />
          ) : undefined
        }
      >
        {recoverable && onRetry && (
          <Button
            onClick={onRetry}
            appearance="primary"
            icon={<ArrowClockwiseRegular />}
            size="small"
          >
            Réessayer
          </Button>
        )}
        {customAction && (
          <Button
            onClick={customAction.handler}
            appearance="secondary"
            size="small"
          >
            {customAction.label}
          </Button>
        )}
      </MessageBarActions>
    </MessageBar>
  );
}
