import { useRef, useState, type ChangeEvent, type FormEvent } from 'react';
import { ChatMultiple24Regular } from '@fluentui/react-icons';
import { PublicHeader } from '../../components/marketing/PublicHeader';
import { PublicFooter } from '../../components/marketing/ClosingSections';
import { ContactSubmissionError, submitContact, type ContactFormValues } from '../../services/contactApi';
import { APP_LOGIN_URL } from '../../config/origins';
import landingStyles from '../landing/LandingPage.module.css';
import styles from './ContactPage.module.css';

const initialValues: ContactFormValues = { name: '', company: '', email: '', phone: '', message: '' };

export function ContactPage({ isAuthenticated }: { isAuthenticated: boolean }) {
  const loginTarget = APP_LOGIN_URL;
  const submissionLock = useRef(false);
  const [values, setValues] = useState(initialValues);
  const [status, setStatus] = useState<'idle' | 'submitting' | 'success' | 'error'>('idle');
  const [feedback, setFeedback] = useState<string | null>(null);

  const updateField = (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    setValues(current => ({ ...current, [event.target.name]: event.target.value }));
    if (status !== 'submitting') {
      setStatus('idle');
      setFeedback(null);
    }
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (submissionLock.current) return;
    submissionLock.current = true;
    setStatus('submitting');
    setFeedback(null);

    try {
      await submitContact(values);
      setValues(initialValues);
      setStatus('success');
      setFeedback('Votre message a bien été envoyé. Nous vous répondrons rapidement.');
    } catch (error) {
      setStatus('error');
      setFeedback(error instanceof ContactSubmissionError
        ? error.message
        : 'Le message n’a pas pu être envoyé. Veuillez réessayer plus tard.');
    } finally {
      submissionLock.current = false;
    }
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
              <label>Nom<input name="name" type="text" autoComplete="name" maxLength={120} required value={values.name} onChange={updateField} disabled={status === 'submitting'} /></label>
              <label>Entreprise<input name="company" type="text" autoComplete="organization" maxLength={200} value={values.company} onChange={updateField} disabled={status === 'submitting'} /></label>
              <label>E-mail<input name="email" type="email" autoComplete="email" maxLength={254} required value={values.email} onChange={updateField} disabled={status === 'submitting'} /></label>
              <label>Téléphone <span>(facultatif)</span><input name="phone" type="tel" autoComplete="tel" maxLength={30} value={values.phone} onChange={updateField} disabled={status === 'submitting'} /></label>
              <label className={styles.messageField}>Message<textarea name="message" rows={6} maxLength={5000} required value={values.message} onChange={updateField} disabled={status === 'submitting'} /></label>
              <button type="submit" disabled={status === 'submitting'}>
                {status === 'submitting' ? 'Envoi en cours…' : 'Envoyer le message'}
              </button>
              {feedback && <p className={`${styles.feedback} ${status === 'success' ? styles.success : styles.error}`}
                role={status === 'error' ? 'alert' : 'status'}>{feedback}</p>}
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
