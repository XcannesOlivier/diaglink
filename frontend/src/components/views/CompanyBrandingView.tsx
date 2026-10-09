import { useRef, useState } from 'react';
import {
  Button, Dialog, DialogActions, DialogBody, DialogContent, DialogSurface, DialogTitle, DialogTrigger,
  Radio, RadioGroup, Spinner, Text, makeStyles, tokens,
} from '@fluentui/react-components';
import { Checkmark16Regular, PaintBrush24Regular } from '@fluentui/react-icons';
import type { CurrentUser } from '../../types/currentUser';
import type { ApiWriteResult } from '../../types/apiResult';
import { deleteLogo, resetBranding, updateAccentColor, uploadLogo, validateCompanyLogo } from '../../services/companyBrandingService';
import { CompanyLogo } from '../layout/CompanyLogo';
import { DialogCloseButton } from '../core/DialogCloseButton';
import { useThemeContext } from '../../contexts/ThemeContext';
import { ViewMessage, ViewRoot } from './ViewLayout';

const PALETTE = [
  { color: '#356A9A', label: 'Bleu' },
  { color: '#39756B', label: 'Vert' },
  { color: '#77639B', label: 'Violet' },
  { color: '#986B36', label: 'Ambre' },
  { color: '#9A5747', label: 'Terracotta' },
  { color: '#32707A', label: 'Pétrole' },
];
const EXTRA_PALETTE = [
  { color: '#285C8C', label: 'Bleu profond' },
  { color: '#315DA8', label: 'Bleu royal' },
  { color: '#254764', label: 'Bleu marine' },
  { color: '#3B6478', label: 'Bleu acier' },
  { color: '#246574', label: 'Cyan profond' },
  { color: '#216B63', label: 'Turquoise' },
  { color: '#286149', label: 'Vert forêt' },
  { color: '#43734A', label: 'Vert mousse' },
  { color: '#52643B', label: 'Olive' },
  { color: '#70632F', label: 'Ocre' },
  { color: '#80602D', label: 'Or foncé' },
  { color: '#8B582A', label: 'Orange foncé' },
  { color: '#89523C', label: 'Cuivre' },
  { color: '#934837', label: 'Terre cuite' },
  { color: '#8C3D3D', label: 'Rouge brique' },
  { color: '#822E36', label: 'Rouge sombre' },
  { color: '#6B3045', label: 'Bordeaux' },
  { color: '#934461', label: 'Rose sombre' },
  { color: '#96365B', label: 'Framboise' },
  { color: '#6D3D70', label: 'Prune' },
  { color: '#66508D', label: 'Améthyste' },
  { color: '#514C87', label: 'Indigo' },
  { color: '#4B5C6B', label: 'Gris bleuté' },
  { color: '#5A626B', label: 'Acier' },
];
const ALL_COLORS = [...PALETTE, ...EXTRA_PALETTE];
type Operation = 'save' | 'upload' | 'delete' | 'reset';
const OPERATION_LABELS: Record<Operation, string> = {
  save: 'Enregistrement…', upload: 'Import du logo…', delete: 'Suppression du logo…', reset: 'Rétablissement…',
};

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, maxWidth: '800px', minWidth: 0 },
  section: {
    display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, minWidth: 0,
    padding: tokens.spacingHorizontalL, border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    '@media (max-width: 768px)': { padding: tokens.spacingHorizontalM },
  },
  heading: { margin: 0, fontSize: tokens.fontSizeBase400, fontWeight: tokens.fontWeightSemibold },
  actions: {
    display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalS,
    '& > button': { maxWidth: '100%', whiteSpace: 'normal', minHeight: '40px' },
  },
  logo: { maxWidth: '160px', maxHeight: '64px' },
  palette: { display: 'flex', flexWrap: 'wrap', columnGap: tokens.spacingHorizontalM },
  colorLabel: { display: 'inline-flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  swatch: { width: '16px', height: '16px', borderRadius: '50%', flexShrink: 0 },
  preview: {
    display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: tokens.spacingHorizontalM,
    padding: tokens.spacingHorizontalM, borderLeft: '3px solid',
    backgroundColor: tokens.colorNeutralBackground2, borderRadius: tokens.borderRadiusMedium,
  },
  feedback: { overflowWrap: 'anywhere' },
  previewName: { minWidth: 0, overflowWrap: 'anywhere' },
  moreColors: { alignSelf: 'flex-start', color: tokens.colorBrandForegroundLink, paddingInline: 0 },
  colorDialog: {
    width: '480px', maxWidth: 'calc(100vw - 24px)', maxHeight: 'min(620px, calc(100dvh - 24px))',
    '@media (max-width: 480px)': { width: 'calc(100vw - 24px)', maxWidth: 'calc(100vw - 24px)' },
  },
  colorBody: { maxHeight: 'min(570px, calc(100dvh - 74px))' },
  colorContent: { minHeight: 0, overflowY: 'auto' },
  colorGrid: {
    display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: tokens.spacingHorizontalS,
    marginTop: tokens.spacingVerticalM,
    '@media (max-width: 480px)': { gridTemplateColumns: 'repeat(3, minmax(0, 1fr))' },
  },
  colorOption: {
    minWidth: 0, minHeight: '54px', padding: tokens.spacingHorizontalXS, whiteSpace: 'normal',
    fontSize: tokens.fontSizeBase200, overflowWrap: 'anywhere',
    '& > span': { display: 'flex', flexDirection: 'column', alignItems: 'center', gap: tokens.spacingVerticalXS },
  },
  colorSelection: { marginTop: tokens.spacingVerticalS, display: 'block', overflowWrap: 'anywhere' },
  colorDialogActions: {
    display: 'flex', flexWrap: 'wrap',
    '& > button': { minWidth: 0, whiteSpace: 'normal', maxWidth: '100%' },
  },
});

interface CompanyBrandingViewProps {
  currentUser: CurrentUser | null;
  logoObjectUrl: string | null;
  getAccessToken: () => Promise<string | null>;
  onCurrentUserRefresh: () => Promise<boolean>;
  onDiagLinkSessionExpired?: () => void;
}

export function CompanyBrandingView(props: CompanyBrandingViewProps) {
  return (
    <ViewRoot title="Personnalisation" subtitle="Adaptez légèrement l’apparence de DiagLink à votre entreprise.">
      {!props.currentUser
        ? <ViewMessage loading message="Chargement de la personnalisation…" />
        : props.currentUser.role !== 'company_admin'
          ? <ViewMessage message="Accès non autorisé." />
          : <BrandingForm key={props.currentUser.companyId} {...props} currentUser={props.currentUser} />}
    </ViewRoot>
  );
}

function BrandingForm({
  currentUser, logoObjectUrl, getAccessToken, onCurrentUserRefresh, onDiagLinkSessionExpired,
}: CompanyBrandingViewProps & { currentUser: CurrentUser }) {
  const styles = useStyles();
  const { themeStyles } = useThemeContext();
  const serverAccent = currentUser.companyBranding?.accentColor ?? null;
  const [draftAccent, setDraftAccent] = useState<string | null>(serverAccent);
  const [operation, setOperation] = useState<Operation | null>(null);
  const [feedback, setFeedback] = useState<{ kind: 'error' | 'success'; message: string } | null>(null);
  const [confirmReset, setConfirmReset] = useState(false);
  const [colorDialogOpen, setColorDialogOpen] = useState(false);
  const [modalAccent, setModalAccent] = useState<string | null>(serverAccent);
  const [needsRefresh, setNeedsRefresh] = useState(false);
  const busyRef = useRef(false);
  const fileInput = useRef<HTMLInputElement>(null);
  const busy = operation !== null;
  const disabled = busy || needsRefresh;
  const hasLogo = currentUser.companyBranding?.hasLogo === true;
  const selectedColor = draftAccent ?? themeStyles.colorBrandForeground1;
  const colors = serverAccent && !PALETTE.some(item => item.color.toLowerCase() === serverAccent.toLowerCase())
    && !EXTRA_PALETTE.some(item => item.color.toLowerCase() === serverAccent.toLowerCase())
    ? [{ color: serverAccent, label: 'Couleur actuelle' }, ...PALETTE]
    : PALETTE;
  const selectedExtra = EXTRA_PALETTE.find(item => item.color.toLowerCase() === draftAccent?.toLowerCase());
  const modalColors = modalAccent && !ALL_COLORS.some(item => item.color.toLowerCase() === modalAccent.toLowerCase())
    ? [{ color: modalAccent, label: 'Couleur actuelle' }, ...ALL_COLORS]
    : ALL_COLORS;
  const modalName = modalColors.find(item => item.color.toLowerCase() === modalAccent?.toLowerCase())?.label;

  const refresh = async () => {
    if (!await onCurrentUserRefresh()) throw new Error('refresh-failed');
    setNeedsRefresh(false);
  };

  const mutate = async (action: Operation, write: () => Promise<ApiWriteResult<unknown>>) => {
    if (busyRef.current || needsRefresh) return;
    busyRef.current = true;
    setOperation(action);
    setFeedback(null);
    let written = false;
    try {
      const result = await write();
      if (result.kind !== 'success') {
        if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) onDiagLinkSessionExpired?.();
        setFeedback({
          kind: 'error',
          message: result.kind === 'unauthorized' ? 'Votre session a expiré. Veuillez vous reconnecter.'
            : result.kind === 'forbidden' ? 'Vous n’êtes pas autorisé à modifier la personnalisation.'
              : result.kind === 'validation-error' ? 'Vérifiez la couleur ou le logo choisi (PNG ou JPEG/JPG, 2 Mo maximum).'
                : 'Impossible d’enregistrer la personnalisation. Réessayez.',
        });
        return;
      }
      written = true;
      if (action === 'reset') setDraftAccent(null);
      await refresh();
      setConfirmReset(false);
      setFeedback({ kind: 'success', message: action === 'reset' ? 'L’apparence DiagLink a été rétablie.' : 'La personnalisation a été mise à jour.' });
    } catch {
      setNeedsRefresh(written);
      setConfirmReset(false);
      setFeedback({
        kind: 'error',
        message: written
          ? 'La modification est enregistrée, mais l’affichage n’a pas pu être actualisé. Actualisez les données.'
          : 'Impossible d’enregistrer la personnalisation. Réessayez.',
      });
    } finally {
      if (action === 'reset') setConfirmReset(false);
      busyRef.current = false;
      setOperation(null);
    }
  };

  const retryRefresh = async () => {
    if (busyRef.current) return;
    busyRef.current = true;
    setOperation('save');
    try {
      await refresh();
      setFeedback({ kind: 'success', message: 'La personnalisation a été actualisée.' });
    } catch {
      setFeedback({ kind: 'error', message: 'Impossible d’actualiser les données. Réessayez.' });
    } finally {
      busyRef.current = false;
      setOperation(null);
    }
  };

  return (
    <div className={styles.form} aria-busy={busy}>
      <section className={styles.section} aria-labelledby="company-logo-heading">
        <h2 className={styles.heading} id="company-logo-heading">Logo de l’entreprise</h2>
        {hasLogo && logoObjectUrl
          ? <CompanyLogo className={styles.logo} logoObjectUrl={logoObjectUrl} companyName={currentUser.companyBranding?.companyName} />
          : <Text>{hasLogo ? 'Chargement du logo…' : 'Aucun logo configuré'}</Text>}
        <Text size={200}>PNG ou JPEG/JPG — 2 Mo maximum. Format horizontal recommandé (environ 3:1 à 4:1). PNG avec fond transparent conseillé.</Text>
        <Text size={200}>Les logos carrés ou verticaux sont acceptés, mais apparaîtront plus petits dans l’en-tête.</Text>
        <Text size={200}>Le logo est mis à jour dès l’import.</Text>
        <input ref={fileInput} type="file" accept="image/png,image/jpeg,.png,.jpg,.jpeg" hidden disabled={disabled}
          aria-label="Fichier du logo"
          onChange={event => {
            const file = event.currentTarget.files?.[0];
            event.currentTarget.value = '';
            if (!file || disabled) return;
            const error = validateCompanyLogo(file);
            if (error) { setFeedback({ kind: 'error', message: error }); return; }
            void mutate('upload', () => uploadLogo(getAccessToken, file));
          }} />
        <div className={styles.actions}>
          <Button disabled={disabled} onClick={() => fileInput.current?.click()}>{hasLogo ? 'Remplacer' : 'Importer un logo'}</Button>
          {hasLogo && <Button disabled={disabled} onClick={() => void mutate('delete', () => deleteLogo(getAccessToken))}>Supprimer</Button>}
        </div>
      </section>

      <section className={styles.section} aria-labelledby="company-accent-heading">
        <h2 className={styles.heading} id="company-accent-heading">Couleur d’accent</h2>
        <Text size={200}>Une touche d’identité, sans modifier le thème DiagLink. Enregistrez pour appliquer votre choix.</Text>
        <RadioGroup value={draftAccent?.toUpperCase() ?? 'default'} disabled={disabled} aria-label="Couleur d’accent"
          onChange={(_event, data) => { setDraftAccent(data.value === 'default' ? null : data.value); setFeedback(null); }}>
          <Radio value="default" label="Apparence DiagLink par défaut" />
          <div className={styles.palette}>
            {colors.map(item => <Radio key={item.color} value={item.color.toUpperCase()} label={
              <span className={styles.colorLabel}><span className={styles.swatch} style={{ backgroundColor: item.color }} aria-hidden="true" />{item.label}</span>
            } />)}
          </div>
        </RadioGroup>
        <Dialog open={colorDialogOpen} onOpenChange={(_event, data) => {
          if (data.open) setModalAccent(draftAccent);
          setColorDialogOpen(data.open);
        }}>
          <DialogTrigger disableButtonEnhancement>
            <Button appearance="transparent" className={styles.moreColors} disabled={disabled}>Plus de choix de couleurs</Button>
          </DialogTrigger>
          <DialogSurface className={styles.colorDialog}>
            <DialogBody className={styles.colorBody}>
              <DialogTitle action={<DialogCloseButton onClick={() => setColorDialogOpen(false)} />}>Choisir une couleur</DialogTitle>
              <DialogContent className={styles.colorContent}>
                <Text size={200}>Sélectionnez une couleur d’accent pour votre entreprise.</Text>
                <div className={styles.colorGrid} role="group" aria-label="Couleurs disponibles">
                  {modalColors.map(item => <Button key={item.color} className={styles.colorOption}
                    aria-label={item.label} aria-pressed={modalAccent?.toLowerCase() === item.color.toLowerCase()}
                    title={`${item.label} — ${item.color}`}
                    appearance={modalAccent?.toLowerCase() === item.color.toLowerCase() ? 'primary' : 'secondary'}
                    onClick={() => setModalAccent(item.color)}>
                    <span><span className={styles.swatch} style={{ backgroundColor: item.color, color: '#ffffff' }} aria-hidden="true">
                      {modalAccent?.toLowerCase() === item.color.toLowerCase() && <Checkmark16Regular />}
                    </span>{item.label}</span>
                  </Button>)}
                </div>
                <Text className={styles.colorSelection} size={200}>Sélection : {modalName ?? 'DiagLink par défaut'}{modalAccent ? ` (${modalAccent})` : ''}</Text>
              </DialogContent>
              <DialogActions className={styles.colorDialogActions}>
                <Button onClick={() => setColorDialogOpen(false)}>Annuler</Button>
                <Button appearance="primary" disabled={disabled || modalAccent === null} onClick={() => {
                  setDraftAccent(modalAccent);
                  setFeedback(null);
                  setColorDialogOpen(false);
                }}>Choisir cette couleur</Button>
              </DialogActions>
            </DialogBody>
          </DialogSurface>
        </Dialog>
        <Text size={200}>Couleur sélectionnée : {selectedExtra ? `${selectedExtra.label} (${draftAccent})` : draftAccent ?? 'DiagLink par défaut'}</Text>
      </section>

      <section className={styles.section} aria-labelledby="company-preview-heading">
        <h2 className={styles.heading} id="company-preview-heading">Aperçu</h2>
        <div className={styles.preview} style={{ borderLeftColor: selectedColor }}>
          <PaintBrush24Regular style={{ color: selectedColor }} aria-hidden="true" />
          <Text className={styles.previewName}>DiagLink · {currentUser.companyBranding?.companyName ?? 'Votre entreprise'}</Text>
          <CompanyLogo logoObjectUrl={logoObjectUrl} companyName={currentUser.companyBranding?.companyName} />
        </div>
      </section>
      {operation && <Spinner size="small" label={OPERATION_LABELS[operation]} />}
      {feedback && <Text className={styles.feedback} role={feedback.kind === 'error' ? 'alert' : 'status'}>{feedback.message}</Text>}
      {needsRefresh && <Button disabled={busy} onClick={() => void retryRefresh()}>Actualiser les données</Button>}
      <div className={styles.actions}>
        <Button appearance="primary" disabled={disabled || draftAccent === serverAccent}
          onClick={() => void mutate('save', () => updateAccentColor(getAccessToken, draftAccent))}>Enregistrer</Button>
        <Button disabled={disabled} onClick={() => setConfirmReset(true)}>Rétablir l’apparence DiagLink</Button>
      </div>
      <Dialog open={confirmReset} onOpenChange={(_event, data) => { if (!busy) setConfirmReset(data.open); }}>
        <DialogSurface>
          <DialogTitle action={<DialogCloseButton disabled={busy} onClick={() => setConfirmReset(false)} />}>Rétablir l’apparence DiagLink ?</DialogTitle>
          <DialogBody>
            <DialogContent>Le logo et la couleur personnalisés seront supprimés pour tous les utilisateurs de l’entreprise.</DialogContent>
            <DialogActions>
              <Button disabled={busy} onClick={() => setConfirmReset(false)}>Annuler</Button>
              <Button appearance="primary" disabled={busy} onClick={() => void mutate('reset', () => resetBranding(getAccessToken))}>Rétablir</Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
}
