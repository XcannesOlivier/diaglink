import type { FormEvent } from 'react';
import { ChatMultiple24Regular } from '@fluentui/react-icons';
import { PublicHeader } from '../../components/marketing/PublicHeader';
import { PublicFooter } from '../../components/marketing/ClosingSections';
import landingStyles from '../landing/LandingPage.module.css';
import styles from './ContactPage.module.css';

export function ContactPage({ isAuthenticated }: { isAuthenticated: boolean }) {
  const loginTarget = isAuthenticated ? '/app' : '/login';
  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
  };

  return (
    <div className={landingStyles.page}>
      <PublicHeader loginTarget={loginTarget} isAuthenticated={isAuthenticated} landingPath="/" />
      <main className={styles.main} id="contact">
        <div className={landingStyles.container}>
          <div className={styles.intro}>
            <p className={landingStyles.eyebrow}>Contact</p>
            <h1>Parlons de votre besoin.</h1>
            <p>Une question sur DiagLink, une machine à intégrer ou envie de voir comment cela fonctionne avec votre documentation ? Contactez-nous.</p>
          </div>
          <div className={styles.grid}>
            <form className={styles.form} onSubmit={handleSubmit}>
              <label>Nom<input name="name" type="text" autoComplete="name" required /></label>
              <label>Entreprise<input name="company" type="text" autoComplete="organization" /></label>
              <label>E-mail<input name="email" type="email" autoComplete="email" required /></label>
              <label>Téléphone <span>(facultatif)</span><input name="phone" type="tel" autoComplete="tel" /></label>
              <label className={styles.messageField}>Message<textarea name="message" rows={6} required /></label>
              <button type="submit">Envoyer le message</button>
            </form>
            <aside className={styles.info}>
              <span className={styles.infoIcon}><ChatMultiple24Regular /></span>
              <h2>Vous souhaitez tester DiagLink ?</h2>
              <p>Indiquez-nous simplement le type de machine concernée. Nous pourrons échanger sur votre besoin avant toute mise en place.</p>
            </aside>
          </div>
        </div>
      </main>
      <PublicFooter loginTarget={loginTarget} />
    </div>
  );
}
