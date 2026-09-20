import React from 'react';
import { makeStyles, mergeClasses, tokens, Text, Spinner } from '@fluentui/react-components';

const useStyles = makeStyles({
  root: {
    height: '100%',
    width: '100%',
    overflow: 'auto',
    padding: `${tokens.spacingVerticalXXL} ${tokens.spacingHorizontalXXL}`,
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
    '@media (max-width: 768px)': {
      padding: `${tokens.spacingVerticalL} ${tokens.spacingHorizontalM}`,
    },
  },
  header: {
    marginBottom: tokens.spacingVerticalL,
  },
  headerWithAction: {
    '@media (max-width: 1000px)': {
      display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: tokens.spacingHorizontalM,
      '& > div': { minWidth: 0, overflowWrap: 'anywhere' },
    },
  },
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
  message: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalS,
    minHeight: '200px',
    borderRadius: tokens.borderRadiusMedium,
    border: `1px dashed ${tokens.colorNeutralStroke2}`,
    color: tokens.colorNeutralForeground3,
  },
});

interface ViewRootProps {
  title: string;
  subtitle?: string;
  headerAction?: React.ReactNode;
  children?: React.ReactNode;
}

/** Shared header + scroll container for the real (non-placeholder) DiagLink views. */
export const ViewRoot: React.FC<ViewRootProps> = ({ title, subtitle, headerAction, children }) => {
  const styles = useStyles();
  return (
    <div className={styles.root}>
      <div className={mergeClasses(styles.header, !!headerAction && styles.headerWithAction)}>
        <div>
        <Text className={styles.title}>{title}</Text>
        {subtitle && <Text className={styles.subtitle}>{subtitle}</Text>}
        </div>
        {headerAction}
      </div>
      {children}
    </div>
  );
};

interface ViewMessageProps {
  message: string;
  loading?: boolean;
}

/** Shared loading/empty/error box — same sober dashed-border style across every view. */
export const ViewMessage: React.FC<ViewMessageProps> = ({ message, loading }) => {
  const styles = useStyles();
  return (
    <div className={styles.message}>
      {loading && <Spinner size="small" />}
      <Text>{message}</Text>
    </div>
  );
};
