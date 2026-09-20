import React from 'react';
import { Text, makeStyles, tokens, Badge } from '@fluentui/react-components';
import { ROLE_LABELS } from '../../utils/navigation';
import type { CurrentUser } from '../../types/currentUser';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    minWidth: 0,
  },
  email: {
    color: tokens.colorNeutralForeground2,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    maxWidth: '220px',
  },
});

interface UserBadgeProps {
  currentUser: CurrentUser | null;
}

/** Discreet header identity — always shows a human-readable role label, never the raw role string. */
export const UserBadge: React.FC<UserBadgeProps> = ({ currentUser }) => {
  const styles = useStyles();

  if (!currentUser) return null;

  return (
    <div className={styles.root}>
      {currentUser.email && <Text className={styles.email}>{currentUser.email}</Text>}
      <Badge appearance="tint" color="informative">
        {ROLE_LABELS[currentUser.role]}
      </Badge>
    </div>
  );
};
