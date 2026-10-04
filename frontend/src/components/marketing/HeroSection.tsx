import { CheckmarkCircle20Filled } from '@fluentui/react-icons';
import { Link } from 'react-router-dom';
import heroImage from '../../assets/marketing/diaglink-hero-final.png';
import styles from '../../pages/landing/LandingPage.module.css';

export function HeroSection({ loginTarget }: { loginTarget: string }) {
  return (
    <section className={styles.hero} id="top">
      <div className={`${styles.container} ${styles.heroGrid}`}>
        <div className={styles.heroCopy}>
          <p className={styles.eyebrow}>Maintenance industrielle</p>
          <h1>La documentation de vos machines devient{' '}<span>interactive.</span></h1>
          <p className={styles.lead}>Posez votre problème. DiagLink analyse la documentation technique de votre machine et vous guide dans le diagnostic, étape par étape.</p>
          <div className={styles.heroActions}>
            <Link className={styles.primaryButton} to="/demonstration">Voir une démonstration</Link>
            <Link className={styles.secondaryButton} to={loginTarget} target="_blank" rel="noopener noreferrer">Se connecter</Link>
          </div>
          <ul className={styles.benefits}>
            {['Réduisez vos temps d’arrêt', 'Accédez à l’information plus rapidement', 'Soutenez vos équipes terrain'].map(item => <li key={item}><CheckmarkCircle20Filled />{item}</li>)}
          </ul>
        </div>
        <div className={styles.heroVisual}>
          <img src={heroImage} alt="DiagLink sur ordinateur et smartphone dans un environnement industriel" />
        </div>
      </div>
    </section>
  );
}
