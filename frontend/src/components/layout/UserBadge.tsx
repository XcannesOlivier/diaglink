import React from 'react';
import { Text, makeStyles, tokens, Badge } from '@fluentui/react-components';
import { ROLE_LABELS } from '../../utils/navigation';
import type { CurrentUser } from '../../types/currentUser';
import { CompanyLogo } from './CompanyLogo';

const useStyles = makeStyles({
  root: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 220px) auto',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    rowGap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  email: {
    gridColumn: 1,
    minWidth: 0,
    color: tokens.colorNeutralForeground2,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    maxWidth: '220px',
  },
  role: {
    gridColumn: 1,
    justifySelf: 'start',
    flexShrink: 0,
    whiteSpace: 'nowrap',
    color: tokens.colorNeutralForeground1,
  },
  logo: {
    gridColumn: 2,
    gridRow: '1 / span 2',
  },
});

interface UserBadgeProps {
  currentUser: CurrentUser | null;
  companyLogoUrl?: string | null;
}

/** Discreet header identity — always shows a human-readable role label, never the raw role string. */
export const UserBadge: React.FC<UserBadgeProps> = ({ currentUser, companyLogoUrl = null }) => {
  const styles = useStyles();

  if (!currentUser) return null;

  return (
    <div className={styles.root}>
      <Text className={styles.email} title={[currentUser.firstName, currentUser.lastName].filter(Boolean).join(' ') || currentUser.email}>
        {[currentUser.firstName, currentUser.lastName].filter(Boolean).join(' ') || currentUser.email}
      </Text>
      <Badge className={styles.role} appearance="outline" color="subtle">
        {ROLE_LABELS[currentUser.role]}
      </Badge>
      {currentUser.role !== 'diaglink_super_admin' && (
        <CompanyLogo className={styles.logo} logoObjectUrl={companyLogoUrl} companyName={currentUser.companyBranding?.companyName} />
      )}
    </div>
  );
};
