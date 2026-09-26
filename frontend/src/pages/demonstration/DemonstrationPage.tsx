import { Play24Filled } from '@fluentui/react-icons';
import { Link } from 'react-router-dom';
import { PublicHeader } from '../../components/marketing/PublicHeader';
import { PublicFooter } from '../../components/marketing/ClosingSections';
import landingStyles from '../landing/LandingPage.module.css';
import styles from './DemonstrationPage.module.css';

// Futures vidéos : importer les MP4 depuis ../../assets/marketing/ puis remplacer null.
const desktopVideoSource: string | null = null;
const mobileVideoSource: string | null = null;

function VideoFrame({ source, label, mobile = false }: { source: string | null; label: string; mobile?: boolean }) {
  return (
    <div className={mobile ? styles.mobileFrame : styles.desktopFrame}>
      {source ? (
        <video controls playsInline preload="metadata" aria-label={label}>
          <source src={source} type="video/mp4" />
        </video>
      ) : (
        <div className={styles.placeholder} aria-label={label}>
          <span className={styles.playIcon}><Play24Filled /></span>
          <p>{label}</p>
        </div>
      )}
    </div>
  );
}

export function DemonstrationPage({ isAuthenticated }: { isAuthenticated: boolean }) {
  const loginTarget = isAuthenticated ? '/app' : '/login';

  return (
    <div className={landingStyles.page}>
      <PublicHeader loginTarget={loginTarget} isAuthenticated={isAuthenticated} landingPath="/" />
      <main>
        <section className={styles.hero} id="top">
          <div className={landingStyles.container}>
            <p className={landingStyles.eyebrow}>Démonstration</p>
            <h1>Découvrez DiagLink en situation réelle.</h1>
            <p>Découvrez comment un technicien peut interroger la documentation de sa machine et retrouver rapidement les informations utiles à son diagnostic.</p>
          </div>
        </section>

        <section className={styles.section}>
          <div className={landingStyles.container}>
            <div className={styles.heading}>
              <h2>DiagLink sur ordinateur</h2>
              <p>Retrouvez la documentation de votre machine, posez votre question et suivez le diagnostic directement depuis votre poste de travail.</p>
            </div>
            <VideoFrame source={desktopVideoSource} label="Démonstration Desktop bientôt disponible" />
          </div>
        </section>

        <section className={`${styles.section} ${styles.mobileSection}`}>
          <div className={`${landingStyles.container} ${styles.mobileLayout}`}>
            <div className={styles.heading}>
              <h2>DiagLink sur mobile</h2>
              <p>Emportez l’assistant technique avec vous et poursuivez votre diagnostic directement au pied de la machine.</p>
            </div>
            <VideoFrame source={mobileVideoSource} label="Démonstration Mobile bientôt disponible" mobile />
          </div>
        </section>

        <section className={styles.cta}>
          <div className={landingStyles.container}>
            <h2>Prêt à utiliser DiagLink sur vos machines ?</h2>
            <Link className={landingStyles.primaryButton} to="/commencer">Configurer ma première machine</Link>
          </div>
        </section>
      </main>
      <PublicFooter loginTarget={loginTarget} />
    </div>
  );
}
