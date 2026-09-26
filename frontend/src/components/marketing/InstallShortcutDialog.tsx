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

export function InstallShortcutDialog({ platform, onClose, browser = detectShortcutBrowser(), showOpenLoginButton = true }: {
  platform: ShortcutPlatform | null;
  onClose: () => void;
  browser?: ShortcutBrowser;
  showOpenLoginButton?: boolean;
}) {
  const mobile = platform === 'ios' || platform === 'android';
  const openLogin = () => window.open('/login', '_blank', 'noopener,noreferrer');
  return (
    <Dialog open={platform !== null} onOpenChange={(_event, data) => !data.open && onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>{mobile ? 'Ajouter DiagLink à l’écran d’accueil' : 'Créer un raccourci DiagLink'}</DialogTitle>
          <DialogContent>
            <p>{showOpenLoginButton
              ? 'Cliquez sur « Ouvrir DiagLink ». Dans le nouvel onglet, créez ensuite un raccourci vers la page de connexion.'
              : 'Créez maintenant un raccourci vers cette page de connexion.'}</p>
            {platform === 'windows' && <>
              {browser === 'edge' && <>
                <p>Dans Microsoft Edge :</p>
                <ol>
                  <li>Cliquez sur les 3 points (…) en haut à droite de Microsoft Edge.</li>
                  <li>Cliquez sur « Outils supplémentaires ».</li>
                  <li>Cliquez sur « Applications ».</li>
                  <li>Cliquez sur « Installer ce site en tant qu’application ».</li>
                  <li>Dans la fenêtre qui s’ouvre, remplacez le nom proposé par « DiagLink », puis cliquez sur « Installer ».</li>
                  <li>Cochez « Créer un raccourci sur le bureau ».</li>
                  <li>Cliquez sur « Autoriser ».</li>
                </ol>
                <p>Une icône DiagLink sera ajoutée sur votre bureau et ouvrira directement la page de connexion.</p>
                <p>Vous pouvez aussi épingler DiagLink à la barre des tâches.</p>
              </>}
              {browser === 'chrome' && <>
                <p>Dans Google Chrome :</p>
                <ol>
                  <li>Cliquez sur le menu ⋮ en haut à droite.</li>
                  <li>Ouvrez « Caster, enregistrer et partager ».</li>
                  <li>Choisissez « Créer un raccourci ».</li>
                  <li>Utilisez le nom « DiagLink ».</li>
                  <li>Si l’option apparaît, laissez « Ouvrir dans une fenêtre » désactivée, puis validez.</li>
                </ol>
              </>}
              {browser !== 'edge' && browser !== 'chrome' && <p>Faites glisser l’adresse ou l’icône située à gauche de l’adresse vers le Bureau, puis renommez le raccourci « DiagLink » si nécessaire.</p>}
            </>}
            {platform === 'macos' && <>
              <p>Dans {browser === 'safari' ? 'Safari' : browser === 'chrome' ? 'Google Chrome' : browser === 'edge' ? 'Microsoft Edge' : browser === 'firefox' ? 'Firefox' : 'votre navigateur'} :</p>
              <ol>
                <li>Sélectionnez l’adresse de la page dans la barre d’adresse.</li>
                <li>Faites glisser l’adresse ou l’icône située à gauche de celle-ci vers le Bureau.</li>
                <li>Renommez le raccourci « DiagLink » si nécessaire.</li>
              </ol>
            </>}
            {platform === 'ios' && <>
              {browser === 'safari' ? <ol>
                <li>Touchez le bouton Partager.</li>
                <li>Choisissez « Ajouter à l’écran d’accueil ».</li>
                <li>Vérifiez que le nom affiché est « DiagLink ».</li>
                <li>Si l’option apparaît, désactivez « Ouvrir comme app web ».</li>
                <li>Touchez « Ajouter ».</li>
              </ol> : <>
                <p>Utilisez le menu Partager de votre navigateur puis choisissez « Ajouter à l’écran d’accueil ».</p>
                <p>Si cette option n’est pas proposée, ouvrez la page dans Safari et utilisez Partager → « Ajouter à l’écran d’accueil ».</p>
              </>}
            </>}
            {platform === 'android' && <>
              <ol>
                <li>Ouvrez le menu {browser === 'samsung' ? '☰' : '⋮'} de votre navigateur.</li>
                <li>Choisissez « Ajouter à l’écran d’accueil » puis « Créer un raccourci » si ce choix est proposé.</li>
                <li>Vérifiez que le nom est « DiagLink ».</li>
                <li>Validez.</li>
              </ol>
              <p>Choisissez uniquement « Créer un raccourci » lorsqu’un choix est proposé.</p>
            </>}
            {platform === 'other' && <p>Utilisez le menu ou le partage de votre navigateur pour créer un raccourci vers cette page. Sur ordinateur, vous pouvez aussi faire glisser l’adresse vers le Bureau.</p>}
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onClose}>Fermer</Button>
            {showOpenLoginButton && <Button appearance="primary" onClick={openLogin}>Ouvrir DiagLink</Button>}
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
