import React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';

const useStyles = makeStyles({
  root: {
    height: '100%',
    width: '100%',
    overflow: 'auto',
    padding: `${tokens.spacingVerticalXXL} ${tokens.spacingHorizontalXXL}`,
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    marginBottom: tokens.spacingVerticalL,
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  headerText: { minWidth: 0 },
  title: {
    fontSize: tokens.fontSizeBase600,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    display: 'block',
  },
  subtitle: {
    marginTop: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground3,
    display: 'block',
  },
  emptyState: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '200px',
    borderRadius: tokens.borderRadiusMedium,
    border: `1px dashed ${tokens.colorNeutralStroke2}`,
    color: tokens.colorNeutralForeground3,
  },
});

interface PlaceholderViewProps {
  title: string;
  subtitle?: string;
  emptyStateMessage?: string;
  children?: React.ReactNode;
  headerAction?: React.ReactNode;
}

/** Shared sober/professional placeholder shell used by every "not yet implemented" DiagLink view. */
export const PlaceholderView: React.FC<PlaceholderViewProps> = ({ title, subtitle, emptyStateMessage, children, headerAction }) => {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      <div className={styles.header} data-page-header>
        <div className={styles.headerText}>
          <Text className={styles.title}>{title}</Text>
          {subtitle && <Text className={styles.subtitle}>{subtitle}</Text>}
        </div>
        {headerAction}
      </div>
      {children}
      {emptyStateMessage && (
        <div className={styles.emptyState}>
          <Text>{emptyStateMessage}</Text>
        </div>
      )}
    </div>
  );
};
