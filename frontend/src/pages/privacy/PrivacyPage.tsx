import { Link } from 'react-router-dom';
import { DocumentLock24Regular } from '@fluentui/react-icons';
import { PublicHeader } from '../../components/marketing/PublicHeader';
import { PublicFooter } from '../../components/marketing/ClosingSections';
import landingStyles from '../landing/LandingPage.module.css';
import styles from './PrivacyPage.module.css';

const collectedData = [
  'Nom et prénom',
  'Adresse e-mail professionnelle',
  'Entreprise associée au compte',
  'Informations relatives aux utilisateurs et aux machines',
  'Données nécessaires à l’authentification',
  'Conversations avec l’assistant technique',
  'Informations techniques liées à l’utilisation du service',
  'Informations nécessaires à la gestion de l’abonnement et de la facturation',
];

const purposes = [
  'Fournir le service DiagLink',
  'Authentifier les utilisateurs',
  'Permettre l’accès aux machines autorisées',
  'Analyser et rechercher des informations dans la documentation technique',
  'Fournir les réponses de l’assistant technique',
  'Assurer le fonctionnement, la sécurité et la maintenance du service',
  'Gérer les abonnements et la facturation',
  'Répondre aux demandes de support ou de contact',
];

const rights = ['Accès', 'Rectification', 'Effacement', 'Limitation', 'Opposition lorsque applicable', 'Portabilité lorsque applicable'];

export function PrivacyPage({ isAuthenticated }: { isAuthenticated: boolean }) {
  const loginTarget = isAuthenticated ? '/app' : '/login';
  return (
    <div className={landingStyles.page}>
      <PublicHeader loginTarget={loginTarget} isAuthenticated={isAuthenticated} landingPath="/" />
      <main className={styles.main}>
        <article className={styles.article}>
          <header className={styles.intro}>
            <p className={landingStyles.eyebrow}>Confidentialité</p>
            <h1>Politique de confidentialité</h1>
            <p>DiagLink accorde une importance particulière à la confidentialité et à la sécurité des données de ses utilisateurs et de ses clients. Cette politique explique quelles données peuvent être traitées dans le cadre de l’utilisation du service et dans quelles finalités.</p>
          </header>

          <section><h2>1. Responsable du traitement</h2><p><strong>DiagLink</strong></p>{/* TODO(juridique): compléter ici l'identité légale et les coordonnées du responsable du traitement. */}</section>
          <section><h2>2. Données pouvant être collectées</h2><p>Selon l’utilisation du service, DiagLink peut traiter notamment les données suivantes :</p><ul>{collectedData.map(item => <li key={item}>{item}</li>)}</ul></section>
          <section className={styles.documentSection}><span className={styles.documentIcon}><DocumentLock24Regular /></span><div><p className={styles.sectionLabel}>3. Documentation technique</p><h2>Vos documents techniques restent vos documents.</h2><p>Les manuels, notices, schémas, vues éclatées et autres documents transmis à DiagLink sont utilisés pour fournir les fonctionnalités du service et permettre à l’assistant technique de rechercher les informations nécessaires.</p></div></section>
          <section><h2>4. Utilisation des données</h2><p>Les données peuvent être utilisées pour les finalités suivantes :</p><ul>{purposes.map(item => <li key={item}>{item}</li>)}</ul></section>
          <section><h2>5. Intelligence artificielle</h2><p>Certaines fonctionnalités de DiagLink utilisent des services d’intelligence artificielle afin d’analyser les demandes des utilisateurs et de rechercher des informations dans la documentation technique associée aux machines.</p></section>
          <section><h2>6. Prestataires techniques</h2><p>DiagLink peut s’appuyer sur des prestataires nécessaires au fonctionnement du service, notamment Microsoft et Azure pour l’infrastructure et les services techniques, ainsi que Stripe pour la gestion des paiements et des abonnements.</p></section>
          <section><h2>7. Conservation des données</h2><p>Les données sont conservées pendant une durée adaptée à leur finalité et aux obligations applicables. Cette durée peut varier selon la nature des données et le contexte de leur traitement.</p></section>
          <section><h2>8. Sécurité et contrôle des accès</h2><p>DiagLink met en œuvre des mesures destinées à limiter l’accès aux données aux utilisateurs et services autorisés, selon leurs droits et les besoins nécessaires au fonctionnement du service.</p></section>
          <section><h2>9. Droits des utilisateurs</h2><p>Selon la réglementation applicable, les utilisateurs peuvent disposer des droits suivants :</p><ul>{rights.map(item => <li key={item}>{item}</li>)}</ul><p>Une demande peut être adressée à DiagLink depuis la <Link to="/contact">page Contact</Link>.</p></section>
          <section><h2>10. Cookies et stockage local</h2><p>Le frontend DiagLink utilise le stockage local du navigateur pour conserver certaines préférences d’interface, le dernier e-mail utilisé, le cache d’authentification Microsoft et certaines informations temporaires liées aux opérations de facturation. Le stockage de session est utilisé pour la session de connexion DiagLink et certaines informations temporaires relatives à la machine active.</p><p>Le service peut également utiliser Microsoft Application Insights pour la télémétrie technique lorsque celui-ci est configuré. Aucun dispositif publicitaire n’est configuré dans le frontend actuel.</p></section>
          <section><h2>11. Modification de la politique</h2><p>Cette politique pourra évoluer en fonction des évolutions du service ou des obligations applicables.</p></section>
          <p className={styles.updated}>Dernière mise à jour : septembre 2026</p>
        </article>
      </main>
      <PublicFooter loginTarget={loginTarget} />
    </div>
  );
}
