import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Dismiss24Regular, Navigation24Regular } from '@fluentui/react-icons';
import logoDiagLink from '../../assets/Logo DiagLink.png';
import styles from '../../pages/landing/LandingPage.module.css';
import { InstallShortcutDialog } from './InstallShortcutDialog';
import { CHAT_ROUTE, detectShortcutPlatform, pendingInstallPlatformKey, type ShortcutPlatform } from '../../utils/installShortcut';

const links = [['Pourquoi DiagLink ?', '#pourquoi'], ['Fonctionnalités', '#fonctionnalites'], ['Comment ça marche ?', '#fonctionnement'], ['Tarifs', '#tarifs']] as const;

export function PublicHeader({ loginTarget, isAuthenticated, landingPath = '' }: { loginTarget: string; isAuthenticated: boolean; landingPath?: string }) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [installDialog, setInstallDialog] = useState<ShortcutPlatform | null>(null);
  const navigate = useNavigate();
  const landingHref = (hash: string) => `${landingPath}${hash}`;
  const install = () => {
    const platform = detectShortcutPlatform();
    setMenuOpen(false);
    if (isAuthenticated) {
      sessionStorage.setItem(pendingInstallPlatformKey, platform);
      navigate(CHAT_ROUTE);
    } else {
      setInstallDialog(platform);
    }
  };
  const continueToLogin = () => {
    if (installDialog === null) return;
    sessionStorage.setItem(pendingInstallPlatformKey, installDialog);
    setInstallDialog(null);
    navigate('/login');
  };
  return (
    <header className={styles.header}>
      <div className={styles.headerInner}>
        <a href="#top" className={styles.brand} aria-label="DiagLink — accueil"><img src={logoDiagLink} alt="DiagLink" /></a>
        <nav className={styles.desktopNav} aria-label="Navigation principale">
          {links.map(([label, href]) => <a key={href} href={landingHref(href)}>{label}</a>)}
        </nav>
        <div className={styles.headerActions}>
          <Link className={styles.primaryButton} to="/demonstration">Voir une démonstration</Link>
          <button className={styles.secondaryButton} type="button" onClick={install}>Installer DiagLink</button>
          <Link className={styles.secondaryButton} to={loginTarget}>Se connecter</Link>
        </div>
        <button className={styles.menuButton} type="button" aria-label={menuOpen ? 'Fermer le menu' : 'Ouvrir le menu'} aria-expanded={menuOpen} onClick={() => setMenuOpen(open => !open)}>
          {menuOpen ? <Dismiss24Regular /> : <Navigation24Regular />}
        </button>
      </div>
      {menuOpen && (
        <nav className={styles.mobileNav} aria-label="Navigation mobile">
          {links.map(([label, href]) => <a key={href} href={landingHref(href)} onClick={() => setMenuOpen(false)}>{label}</a>)}
          <Link className={styles.primaryButton} to="/demonstration" onClick={() => setMenuOpen(false)}>Voir une démonstration</Link>
          <button className={styles.secondaryButton} type="button" onClick={install}>Installer DiagLink</button>
          <Link className={styles.secondaryButton} to={loginTarget} onClick={() => setMenuOpen(false)}>Se connecter</Link>
        </nav>
      )}
      <InstallShortcutDialog platform={installDialog} authenticationRequired={!isAuthenticated}
        onContinue={continueToLogin} onClose={() => setInstallDialog(null)} />
    </header>
  );
}
