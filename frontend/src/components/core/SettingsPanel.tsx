import React from 'react';
import {
  Drawer,
  DrawerHeader,
  DrawerHeaderTitle,
  DrawerBody,
  Button,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Dismiss24Regular, EditRegular, PaintBrush20Regular } from '@fluentui/react-icons';
import { ThemePicker } from './ThemePicker';
import { UserProfileEditDialog } from './UserProfileEditDialog';
import { updateCompanyUser } from '../../services/userService';
import type { CurrentUser } from '../../types/currentUser';

interface SettingsPanelProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
  currentUser: CurrentUser | null;
  getAccessToken: () => Promise<string | null>;
  onCurrentUserRefresh: () => Promise<void>;
  onDiagLinkSessionExpired?: () => void;
  onOpenPersonalization: () => void;
}

const useStyles = makeStyles({
  drawer: {
    width: 'min(320px, 100vw)',
  },
  section: {
    marginBottom: tokens.spacingVerticalXXL,
  },
  sectionTitle: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    marginBottom: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground1,
  },
  drawerBody: {
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
    overflowY: 'auto',
  },
  profileActionArea: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    paddingTop: '24px',
    paddingBottom: '24px',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  profileAction: {
    width: '100%',
    justifyContent: 'flex-start',
    color: tokens.colorNeutralForeground1,
    cursor: 'pointer',
  },
  profileEmail: {
    display: 'block',
    marginTop: tokens.spacingVerticalXS,
    paddingInline: tokens.spacingHorizontalM,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    overflowWrap: 'anywhere',
    wordBreak: 'break-word',
  },
});

export const SettingsPanel: React.FC<SettingsPanelProps> = ({
  isOpen,
  onOpenChange,
  currentUser,
  getAccessToken,
  onCurrentUserRefresh,
  onDiagLinkSessionExpired,
  onOpenPersonalization,
}) => {
  const styles = useStyles();
  const [profileDialogOpen, setProfileDialogOpen] = React.useState(false);

  return (
    <Drawer
      open={isOpen}
      onOpenChange={(_, { open }) => onOpenChange(open)}
      position="end"
      className={styles.drawer}
    >
      <DrawerHeader>
        <DrawerHeaderTitle
          action={
            <Button
              appearance="subtle"
              aria-label="Fermer"
              icon={<Dismiss24Regular />}
              onClick={() => onOpenChange(false)}
            />
          }
        >
          Paramètres
        </DrawerHeaderTitle>
      </DrawerHeader>

      <DrawerBody className={styles.drawerBody}>
        <div className={styles.section}>
          <div className={styles.sectionTitle}>Apparence</div>
          <ThemePicker />
        </div>
        {currentUser?.role === 'company_admin' && (
          <div className={styles.profileActionArea}>
            <Button appearance="subtle" icon={<PaintBrush20Regular />} className={styles.profileAction}
              onClick={() => { onOpenChange(false); onOpenPersonalization(); }}>
              Personnalisation
            </Button>
            <div>
              <Button appearance="subtle" icon={<EditRegular />} className={styles.profileAction} onClick={() => setProfileDialogOpen(true)}>
                Modifier mes informations
              </Button>
              {currentUser.email && (
                <Text className={styles.profileEmail}>{currentUser.email}</Text>
              )}
            </div>
          </div>
        )}
      </DrawerBody>
      <UserProfileEditDialog
        open={profileDialogOpen}
        title="Modifier mes informations"
        user={currentUser ? { ...currentUser, id: currentUser.userId } : null}
        onOpenChange={setProfileDialogOpen}
        onSave={request => currentUser
          ? updateCompanyUser(getAccessToken, currentUser.userId, request)
          : Promise.resolve({ kind: 'error' })}
        onSaved={onCurrentUserRefresh}
        onDiagLinkSessionExpired={onDiagLinkSessionExpired}
      />
    </Drawer>
  );
};
