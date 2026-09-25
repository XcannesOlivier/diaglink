import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
} from '@fluentui/react-components';
import { detectShortcutBrowser, type ShortcutBrowser, type ShortcutPlatform } from '../../utils/installShortcut';

export function InstallShortcutDialog({ platform, onClose, browser = detectShortcutBrowser(), authenticationRequired = false, onContinue }: {
  platform: ShortcutPlatform | null;
  onClose: () => void;
  browser?: ShortcutBrowser;
  authenticationRequired?: boolean;
  onContinue?: () => void;
}) {
  const mobile = platform === 'ios' || platform === 'android';
  return (
    <Dialog open={platform !== null} onOpenChange={(_event, data) => !data.open && onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>{authenticationRequired ? 'Installer DiagLink' : mobile ? 'Ajouter DiagLink à l’écran d’accueil' : platform === 'windows' ? 'Installer DiagLink sur votre ordinateur' : 'Installer DiagLink'}</DialogTitle>
          <DialogContent>
            {authenticationRequired && <>
              <p>Pour ajouter DiagLink à votre ordinateur ou à votre écran d’accueil, connectez-vous d’abord à votre espace.</p>
              <p>Après votre connexion, nous vous guiderons pour créer le raccourci vers votre assistant technique.</p>
            </>}
            {!authenticationRequired && platform === 'windows' && <>
              <p>Ajoutez DiagLink à votre ordinateur pour accéder directement à votre assistant technique.</p>
              {browser === 'edge' && <>
                <p>Dans Microsoft Edge :</p>
                <ol>
                  <li>Cliquez sur le menu ⋯ en haut à droite.</li>
                  <li>Ouvrez « Applications ».</li>
                  <li>Choisissez « Installer ce site en tant qu’application ».</li>
                  <li>Nommez l’application « DiagLink » si nécessaire.</li>
                  <li>Une fois installée, choisissez « Créer un raccourci sur le Bureau » si Edge propose cette option.</li>
                </ol>
                <p>Selon votre version d’Edge, cette option peut se trouver dans « Autres outils » → « Applications ».</p>
              </>}
              {browser === 'chrome' && <>
                <p>Dans Google Chrome :</p>
                <ol>
                  <li>Cliquez sur le menu ⋮ en haut à droite.</li>
                  <li>Recherchez l’option permettant d’installer la page ou de créer un raccourci.</li>
                  <li>Choisissez « Installer la page en tant qu’application » ou « Créer un raccourci » selon la version de Chrome.</li>
                  <li>Utilisez le nom « DiagLink ».</li>
                  <li>Validez la création du raccourci.</li>
                </ol>
              </>}
              {browser === 'other' && <p>Utilisez le menu de votre navigateur pour créer un raccourci vers cette page ou l’installer comme application.</p>}
            </>}
            {!authenticationRequired && platform === 'ios' && <ol>
              <li>Ouvrez cette page dans Safari.</li>
              <li>Touchez le bouton Partager.</li>
              <li>Choisissez « Ajouter à l’écran d’accueil ».</li>
              <li>Vérifiez que le nom affiché est « DiagLink ».</li>
              <li>Touchez « Ajouter ».</li>
            </ol>}
            {!authenticationRequired && platform === 'ios' && <p>Si vous utilisez Chrome, Edge ou un navigateur intégré, ouvrez d’abord cette page dans Safari. Une icône DiagLink apparaîtra sur votre écran d’accueil et vous donnera accès directement à votre assistant.</p>}
            {!authenticationRequired && platform === 'android' && <>
              <ol>
                <li>Ouvrez le menu ⋮ de Chrome.</li>
                <li>Choisissez « Ajouter à l’écran d’accueil » ou « Créer un raccourci » selon la version de Chrome.</li>
                <li>Vérifiez que le nom est « DiagLink ».</li>
                <li>Validez.</li>
              </ol>
              <p>Une icône DiagLink apparaîtra sur votre écran d’accueil et ouvrira directement votre assistant technique.</p>
            </>}
            {!authenticationRequired && platform === 'other' && <p>Créez un raccourci vers DiagLink depuis le menu de votre navigateur pour accéder directement à votre assistant technique.</p>}
          </DialogContent>
          <DialogActions>
            {authenticationRequired ? <>
              <Button appearance="secondary" onClick={onClose}>Annuler</Button>
              <Button appearance="primary" onClick={onContinue}>Se connecter et continuer</Button>
            </> : <Button appearance="primary" onClick={onClose}>J’ai compris</Button>}
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
