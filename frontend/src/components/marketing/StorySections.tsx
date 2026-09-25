import { Clock24Regular, DocumentMultiple24Regular, Warning24Regular, Wrench24Regular, Document24Regular, Search24Regular, Chat24Regular } from '@fluentui/react-icons';
import { DiagnosticCard } from './VisualMockups';
import styles from '../../pages/landing/LandingPage.module.css';

export function ProblemSection() {
  const cards = [
    { icon: <DocumentMultiple24Regular />, title: 'Documentation dispersée', text: 'Multiples fichiers, formats et sources.' },
    { icon: <Clock24Regular />, title: 'Diagnostic chronophage', text: 'Des heures à chercher la bonne information.' },
    { icon: <Warning24Regular />, title: 'Temps d’arrêt machine', text: 'Une perte de production coûteuse.' },
  ];
  return <section className={styles.section} id="pourquoi"><div className={`${styles.container} ${styles.split}`}><div><p className={styles.eyebrow}>Le problème</p><h2>Vos techniciens ont déjà l’information. Encore faut-il la trouver.</h2><p className={styles.sectionText}>Manuel de 400 pages, schéma électrique, vue éclatée, référence d’une pièce… La documentation est souvent dispersée et difficile d’accès. Résultat : des diagnostics plus longs et des machines à l’arrêt.</p></div><div className={styles.problemCards}>{cards.map(card => <article className={styles.problemCard} key={card.title}><span className={styles.iconBox}>{card.icon}</span><div><h3>{card.title}</h3><p>{card.text}</p></div></article>)}</div></div></section>;
}

export function DiagnosticDemo() {
  return <section className={`${styles.section} ${styles.tintSection}`}><div className={`${styles.container} ${styles.demoGrid}`}><div><p className={styles.eyebrow}>Démonstration</p><h2>Un diagnostic concret, en quelques secondes.</h2><p className={styles.sectionText}>DiagLink vous fournit des réponses claires, étape par étape, avec la source exacte dans vos documents.</p><p className={styles.staticNote}>Représentation statique d’un exemple de diagnostic</p></div><DiagnosticCard /></div></section>;
}

export function HowItWorksSection() {
  const steps = [{ icon: <Wrench24Regular />, title: 'Vos machines', text: 'Vous ajoutez vos machines dans DiagLink.' }, { icon: <Document24Regular />, title: 'Leur documentation', text: 'Vous importez leur documentation technique : PDF, manuels, schémas…' }, { icon: <Search24Regular />, title: 'DiagLink l’analyse', text: 'DiagLink analyse et organise les informations pour chaque machine.' }, { icon: <Chat24Regular />, title: 'Vos techniciens interrogent la machine', text: 'Ils posent leurs questions en langage naturel et obtiennent des réponses avec leurs sources.' }];
  return <section className={styles.section} id="fonctionnement"><div className={styles.container}><div className={styles.sectionHeading}><p className={styles.eyebrow}>Comment ça marche ?</p><h2>Une approche simple et efficace.</h2></div><div className={styles.steps}>{steps.map((step, index) => <article className={styles.step} key={step.title}><span className={styles.stepNumber}>{index + 1}</span><span className={styles.stepIcon}>{step.icon}</span><h3>{step.title}</h3><p>{step.text}</p></article>)}</div><div className={styles.infoBanner}><DocumentMultiple24Regular /><strong>Chaque machine possède sa propre base documentaire.</strong></div></div></section>;
}
