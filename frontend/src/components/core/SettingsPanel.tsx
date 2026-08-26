import React from 'react';
import {
  Drawer,
  DrawerHeader,
  DrawerHeaderTitle,
  DrawerBody,
  Button,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Dismiss24Regular } from '@fluentui/react-icons';
import { ThemePicker } from './ThemePicker';

interface SettingsPanelProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
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
});

export const SettingsPanel: React.FC<SettingsPanelProps> = ({ isOpen, onOpenChange }) => {
  const styles = useStyles();

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

      <DrawerBody>
        <div className={styles.section}>
          <div className={styles.sectionTitle}>Apparence</div>
          <ThemePicker />
        </div>
      </DrawerBody>
    </Drawer>
  );
};