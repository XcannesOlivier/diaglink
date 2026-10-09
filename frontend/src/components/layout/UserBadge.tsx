import React from 'react';
import { Text, makeStyles, tokens, Badge } from '@fluentui/react-components';
import { ROLE_LABELS } from '../../utils/navigation';
import type { CurrentUser } from '../../types/currentUser';
import { CompanyLogo } from './CompanyLogo';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    minWidth: 0,
  },
  email: {
    minWidth: 0,
    color: tokens.colorNeutralForeground2,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    maxWidth: '220px',
  },
  role: {
    flexShrink: 0,
    whiteSpace: 'nowrap',
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
      <Text className={styles.email}>
        {[currentUser.firstName, currentUser.lastName].filter(Boolean).join(' ') || currentUser.email}
      </Text>
      <Badge className={styles.role} appearance="tint" color="informative">
        {ROLE_LABELS[currentUser.role]}
      </Badge>
      {currentUser.role !== 'diaglink_super_admin' && (
        <CompanyLogo logoObjectUrl={companyLogoUrl} companyName={currentUser.companyBranding?.companyName} />
      )}
    </div>
  );
};
