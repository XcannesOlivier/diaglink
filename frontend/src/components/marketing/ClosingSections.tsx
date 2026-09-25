import { Checkmark20Regular } from '@fluentui/react-icons';
import { Link } from 'react-router-dom';
import logoDiagLink from '../../assets/Logo DiagLink.png';
import styles from '../../pages/landing/LandingPage.module.css';

export function PricingSection() {
  const items = ['Un tarif clair', 'Crédit d’utilisation inclus', 'Crédit supplémentaire disponible si nécessaire', 'Préparation des documents techniques facturée une seule fois', 'Coût de préparation calculé avant paiement'];
  return <section className={`${styles.section} ${styles.pricingSection}`} id="tarifs"><div className={`${styles.container} ${styles.pricingGrid}`}><div><p className={styles.eyebrow}>Tarifs</p><h2>Un prix simple et transparent.</h2><div className={styles.price}><strong>29,90 € HT</strong><span>/ mois / machine</span></div><p className={styles.priceNote}>Crédit d’utilisation inclus.</p><p className={styles.preparationNote}>Préparation documentaire : 99,90 € HT jusqu’à 400 pages incluses, puis 0,27 € HT par page supplémentaire.</p></div><ul className={styles.priceList}>{items.map(item => <li key={item}><Checkmark20Regular />{item}</li>)}</ul></div></section>;
}

export function FinalCta() {
  return <section className={styles.ctaSection} id="contact"><div className={styles.ctaGlow} /><div className={styles.container}><p className={styles.eyebrowLight}>Prêt à utiliser DiagLink ?</p><h2>Configurez votre première machine.</h2><p>Transmettez vos documents techniques, obtenez le coût de préparation et soumettez votre machine pour validation.</p><div className={styles.ctaActions}><Link className={styles.lightButton} to="/commencer">Configurer ma première machine</Link><Link className={styles.ctaSecondaryButton} to="/demonstration">Voir une démonstration</Link></div><small className={styles.ctaNote}>Votre abonnement est activé après validation de la documentation technique.</small></div></section>;
}

export function PublicFooter({ loginTarget }: { loginTarget: string }) {
  return <footer className={styles.footer}><div className={styles.container}><img src={logoDiagLink} alt="DiagLink" /><nav aria-label="Liens de pied de page"><Link to="/mentions-legales">Mentions légales</Link><Link to="/confidentialite">Confidentialité</Link><Link to="/contact">Contact</Link><Link to={loginTarget}>Connexion</Link></nav><small>© {new Date().getFullYear()} DiagLink</small></div></footer>;
}
