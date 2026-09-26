import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Dismiss24Regular, Navigation24Regular } from '@fluentui/react-icons';
import logoDiagLink from '../../assets/Logo DiagLink.png';
import styles from '../../pages/landing/LandingPage.module.css';

const links = [['Pourquoi DiagLink ?', '#pourquoi'], ['Fonctionnalités', '#fonctionnalites'], ['Comment ça marche ?', '#fonctionnement'], ['Tarifs', '#tarifs']] as const;

export function PublicHeader({ loginTarget, landingPath = '' }: { loginTarget: string; isAuthenticated: boolean; landingPath?: string }) {
  const [menuOpen, setMenuOpen] = useState(false);
  const landingHref = (hash: string) => `${landingPath}${hash}`;
  const install = () => {
    setMenuOpen(false);
    window.open('/login?install=1', '_blank', 'noopener,noreferrer');
  };
  return (
    <header className={styles.header}>
      <div className={styles.headerInner}>
        <a href="#top" className={styles.brand} aria-label="DiagLink — accueil"><img src={logoDiagLink} alt="DiagLink" /></a>
        <nav className={styles.desktopNav} aria-label="Navigation principale">
          {links.map(([label, href]) => <a key={href} href={landingHref(href)}>{label}</a>)}
        </nav>
        <div className={styles.headerActions}>
          <Link className={styles.primaryButton} to="/commencer">Configurer ma première machine</Link>
          <button className={styles.secondaryButton} type="button" onClick={install}>Installer DiagLink</button>
        </div>
        <button className={styles.menuButton} type="button" aria-label={menuOpen ? 'Fermer le menu' : 'Ouvrir le menu'} aria-expanded={menuOpen} onClick={() => setMenuOpen(open => !open)}>
          {menuOpen ? <Dismiss24Regular /> : <Navigation24Regular />}
        </button>
      </div>
      {menuOpen && (
        <nav className={styles.mobileNav} aria-label="Navigation mobile">
          {links.map(([label, href]) => <a key={href} href={landingHref(href)} onClick={() => setMenuOpen(false)}>{label}</a>)}
          <Link className={styles.primaryButton} to="/commencer" onClick={() => setMenuOpen(false)}>Configurer ma première machine</Link>
          <button className={styles.secondaryButton} type="button" onClick={install}>Installer DiagLink</button>
          <Link className={styles.secondaryButton} to={loginTarget} target="_blank" rel="noopener noreferrer" onClick={() => setMenuOpen(false)}>Se connecter</Link>
        </nav>
      )}
    </header>
  );
}
