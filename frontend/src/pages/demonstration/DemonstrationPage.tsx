import { Play24Filled } from '@fluentui/react-icons';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { PublicHeader } from '../../components/marketing/PublicHeader';
import { PublicFooter } from '../../components/marketing/ClosingSections';
import { APP_LOGIN_URL } from '../../config/origins';
import landingStyles from '../landing/LandingPage.module.css';
import styles from './DemonstrationPage.module.css';

const desktopVideoEmbedUrl = 'https://www.youtube-nocookie.com/embed/CU0pD2v5pvI';
const desktopVideoThumbnailUrl = 'https://i.ytimg.com/vi/CU0pD2v5pvI/maxresdefault.jpg';

// Future vidéo mobile : importer le MP4 depuis ../../assets/marketing/ puis remplacer null.
const mobileVideoSource: string | null = null;

function DesktopVideoFrame() {
  const [isPlayerLoaded, setIsPlayerLoaded] = useState(false);

  return (
    <div className={styles.desktopFrame}>
      {isPlayerLoaded ? (
        <iframe
          src={desktopVideoEmbedUrl}
          title="Démonstration DiagLink sur ordinateur"
          allow="accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
          referrerPolicy="strict-origin-when-cross-origin"
          allowFullScreen
        />
      ) : (
        <button
          type="button"
          className={`${styles.placeholder} ${styles.videoPlaceholder}`}
          aria-label="Lire la démonstration DiagLink sur ordinateur"
          onClick={() => setIsPlayerLoaded(true)}
        >
          <img
            className={styles.videoThumbnail}
            src={desktopVideoThumbnailUrl}
            alt="Aperçu de la démonstration DiagLink sur ordinateur"
            loading="lazy"
            decoding="async"
            onError={({ currentTarget }) => { currentTarget.hidden = true; }}
          />
          <span className={styles.playIcon}><Play24Filled /></span>
          <p>Lire la démonstration Desktop</p>
        </button>
      )}
    </div>
  );
}

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
  const loginTarget = APP_LOGIN_URL;

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
            <DesktopVideoFrame />
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
