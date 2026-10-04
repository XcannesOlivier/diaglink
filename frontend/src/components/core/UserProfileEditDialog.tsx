import React, { useEffect, useRef, useState } from 'react';
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Input,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import type { ApiWriteResult } from '../../types/apiResult';
import type { CompanyUserDto } from '../../types/company';
import type { UpdateUserProfileRequest } from '../../services/userService';
import { DialogCloseButton } from './DialogCloseButton';

export interface EditableUserProfile {
  id: string;
  firstName?: string | null;
  lastName?: string | null;
  phoneNumber?: string | null;
}

interface UserProfileEditDialogProps {
  open: boolean;
  title: string;
  user: EditableUserProfile | null;
  onOpenChange: (open: boolean) => void;
  onSave: (request: UpdateUserProfileRequest) => Promise<ApiWriteResult<CompanyUserDto>>;
  onSaved: (user: CompanyUserDto) => Promise<void> | void;
  onDiagLinkSessionExpired?: () => void;
}

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  error: { color: tokens.colorPaletteRedForeground1 },
});

export const UserProfileEditDialog: React.FC<UserProfileEditDialogProps> = ({
  open,
  title,
  user,
  onOpenChange,
  onSave,
  onSaved,
  onDiagLinkSessionExpired,
}) => {
  const styles = useStyles();
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [phoneNumber, setPhoneNumber] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const savingRef = useRef(false);

  useEffect(() => {
    if (!open || !user) return;
    setFirstName(user.firstName ?? '');
    setLastName(user.lastName ?? '');
    setPhoneNumber(user.phoneNumber ?? '');
    setError(null);
  }, [open, user]);

  const handleSave = async () => {
    if (!user || savingRef.current) return;

    const request = {
      firstName: firstName.trim(),
      lastName: lastName.trim(),
      phoneNumber: phoneNumber.trim(),
    };
    if (!request.firstName) return setError('Le prénom est obligatoire.');
    if (!request.lastName) return setError('Le nom est obligatoire.');
    if (!request.phoneNumber) return setError('Le téléphone est obligatoire.');
    if (request.firstName.length > 100) return setError('Le prénom est trop long.');
    if (request.lastName.length > 100) return setError('Le nom est trop long.');
    if (request.phoneNumber.length > 30) return setError('Le téléphone est trop long.');

    savingRef.current = true;
    setSaving(true);
    setError(null);
    const result = await onSave(request);

    if (result.kind === 'success') {
      await onSaved(result.data);
      savingRef.current = false;
      setSaving(false);
      onOpenChange(false);
      return;
    }

    if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) {
      onDiagLinkSessionExpired?.();
    }
    setError(
      result.kind === 'validation-error' || result.kind === 'conflict'
        ? result.message
        : result.kind === 'forbidden'
          ? "Vous n'êtes pas autorisé à modifier cet utilisateur."
          : result.kind === 'not-found'
            ? 'Utilisateur introuvable.'
            : "Impossible d'enregistrer les modifications."
    );
    savingRef.current = false;
    setSaving(false);
  };

  return (
    <Dialog open={open} onOpenChange={(_event, data) => !saving && onOpenChange(data.open)}>
      <DialogSurface>
        <DialogTitle action={<DialogCloseButton disabled={saving} onClick={() => onOpenChange(false)} />}>{title}</DialogTitle>
        <DialogBody>
          <DialogContent className={styles.form}>
            <Field label="Prénom" required>
              <Input value={firstName} maxLength={100} disabled={saving} onChange={(_event, data) => setFirstName(data.value)} />
            </Field>
            <Field label="Nom" required>
              <Input value={lastName} maxLength={100} disabled={saving} onChange={(_event, data) => setLastName(data.value)} />
            </Field>
            <Field label="Téléphone" required>
              <Input type="tel" value={phoneNumber} maxLength={30} disabled={saving} onChange={(_event, data) => setPhoneNumber(data.value)} />
            </Field>
            {error && <Text className={styles.error}>{error}</Text>}
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" disabled={saving} onClick={() => onOpenChange(false)}>Annuler</Button>
            <Button appearance="primary" disabled={saving} onClick={handleSave}>
              {saving ? <><Spinner size="tiny" /> Enregistrement…</> : 'Enregistrer'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};
