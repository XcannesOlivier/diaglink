import { BookOpen24Regular, CheckmarkCircle24Regular, ClipboardTaskListLtr24Regular, Desktop24Regular, DocumentBulletList24Regular, DocumentSearch24Regular, LockClosed24Regular, People24Regular, Phone24Regular, Settings24Regular, Tablet24Regular, Wrench24Regular } from '@fluentui/react-icons';
import fieldTabletImage from '../../assets/marketing/diaglink-field-tablet.png';
import styles from '../../pages/landing/LandingPage.module.css';

export function FeaturesSection() {
  const features = [
    { icon: <Wrench24Regular />, title: 'Pannes et procédures de diagnostic' },
    { icon: <DocumentSearch24Regular />, title: 'Schémas électriques' },
    { icon: <ClipboardTaskListLtr24Regular />, title: 'Procédures de maintenance' },
    { icon: <Settings24Regular />, title: 'Références de pièces' },
    { icon: <CheckmarkCircle24Regular />, title: 'Valeurs et points de contrôle' },
    { icon: <BookOpen24Regular />, title: 'Pages exactes de la documentation' },
  ];
  return <section className={`${styles.section} ${styles.featuresSection}`} id="fonctionnalites"><div className={styles.container}><div className={styles.sectionHeading}><p className={styles.eyebrow}>Ce que DiagLink peut retrouver</p><h2>Toutes les informations utiles, au même endroit.</h2></div><div className={styles.featureGrid}>{features.map(feature => <article className={styles.featureCard} key={feature.title}><span className={styles.iconBox}>{feature.icon}</span><h3>{feature.title}</h3></article>)}</div></div></section>;
}

function TabletComposition() {
  return <div className={styles.fieldVisual}><img src={fieldTabletImage} alt="DiagLink sur tablette avec le Compresseur Atlas GA90 sélectionné" /></div>;
}

export function FieldSection() {
  const uses = [{ icon: <Wrench24Regular />, text: 'Sur le terrain' }, { icon: <Settings24Regular />, text: 'En atelier' }, { icon: <Desktop24Regular />, text: 'Sur tous vos appareils' }];
  return <section className={`${styles.section} ${styles.fieldSection}`}><div className={`${styles.container} ${styles.fieldGrid}`}><TabletComposition /><div><p className={styles.eyebrow}>Pensé pour le technicien sur le terrain</p><h2>L’information qu’il vous faut, où que vous soyez.</h2><p className={styles.sectionText}>Sélectionnez votre machine, décrivez le problème et poursuivez votre diagnostic directement depuis votre téléphone, votre tablette ou votre PC.</p><div className={styles.useCases}>{uses.map(use => <span key={use.text}>{use.icon}{use.text}</span>)}</div><div className={styles.devices} aria-hidden="true"><Phone24Regular /><Tablet24Regular /><Desktop24Regular /></div></div></div></section>;
}

export function TrustSection() {
  const cards = [{ icon: <DocumentBulletList24Regular />, title: 'Documentation propre à l’entreprise' }, { icon: <BookOpen24Regular />, title: 'Sources consultables — page et document' }, { icon: <People24Regular />, title: 'Accès utilisateurs contrôlés' }];
  return <section className={styles.section}><div className={styles.container}><div className={styles.sectionHeading}><p className={styles.eyebrow}>Vos données, votre contrôle</p><h2>DiagLink s’appuie sur votre documentation technique.</h2><p className={styles.sectionText}>Des réponses basées sur vos propres documents, avec des sources consultables et des accès contrôlés.</p></div><div className={styles.trustGrid}>{cards.map(card => <article className={styles.trustCard} key={card.title}><span className={styles.iconBox}>{card.icon}</span><h3>{card.title}</h3>{card.title.includes('Accès') && <LockClosed24Regular className={styles.cornerIcon} />}</article>)}</div></div></section>;
}
