import { DocumentLock24Regular } from '@fluentui/react-icons';
import { PublicHeader } from '../../components/marketing/PublicHeader';
import { PublicFooter } from '../../components/marketing/ClosingSections';
import { APP_LOGIN_URL } from '../../config/origins';
import landingStyles from '../landing/LandingPage.module.css';
import styles from './PrivacyPage.module.css';

const collectedData = [
  { label: 'Données d’identification :', description: 'nom et prénom ;' },
  { label: 'Coordonnées professionnelles :', description: 'adresse e-mail ;' },
  { label: 'Données professionnelles :', description: 'entreprise associée au compte et rôle de l’utilisateur ;' },
  { label: 'Données de compte :', description: 'identifiants et informations liées à l’authentification ;' },
  { label: 'Données liées à l’utilisation du service :', description: 'machines accessibles par l’utilisateur et informations associées à son compte ;' },
  { label: 'Contenu des échanges avec l’assistant technique :', description: 'messages et informations saisis par l’utilisateur ;' },
  { label: 'Données techniques :', description: 'journaux de connexion et d’utilisation, adresse IP et informations relatives au navigateur ou à l’appareil lorsqu’elles sont collectées ;' },
  { label: 'Données de facturation :', description: 'informations relatives aux abonnements, crédits, factures et paiements.' },
];

const purposes = [
  {
    purpose: 'Fourniture du service DiagLink, gestion des comptes, authentification et accès aux machines autorisées',
    basis: 'traitement nécessaire à l’exécution du contrat ;',
  },
  {
    purpose: 'Traitement des demandes adressées à l’assistant technique et recherche dans la documentation associée aux machines',
    basis: 'traitement nécessaire à la fourniture du service ;',
  },
  {
    purpose: 'Gestion des abonnements, crédits, facturation et paiements',
    basis: 'traitement nécessaire à l’exécution de la relation contractuelle et, lorsque applicable, au respect des obligations légales ;',
  },
  {
    purpose: 'Sécurité, prévention des abus, diagnostic des incidents et maintien du bon fonctionnement du service',
    basis: 'traitement fondé sur l’intérêt légitime de DiagLink à assurer la sécurité et la fiabilité de son service ;',
  },
  {
    purpose: 'Gestion des demandes de support et de contact',
    basis: 'traitement nécessaire à la gestion de la relation avec les utilisateurs et clients.',
  },
];

const rights = [
  { label: 'Droit d’accès', description: 'à leurs données personnelles ;' },
  { label: 'Droit de rectification', description: 'des données inexactes ou incomplètes ;' },
  { label: 'Droit à l’effacement', description: 'de leurs données, lorsque les conditions applicables sont réunies ;' },
  { label: 'Droit à la limitation', description: 'du traitement ;' },
  { label: 'Droit d’opposition', description: 'lorsque le traitement repose notamment sur l’intérêt légitime ;' },
  { label: 'Droit à la portabilité', description: 'lorsque les conditions prévues par la réglementation sont réunies.' },
];

export function PrivacyPage({ isAuthenticated }: { isAuthenticated: boolean }) {
  const loginTarget = APP_LOGIN_URL;
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

          <section>
            <h2>1. Responsable du traitement</h2>
            <p>Le service <strong>DiagLink</strong> est exploité par <strong>XCANNES LLC</strong>, société enregistrée dans l’État du Delaware, aux États-Unis.</p>
            <p>
              <strong>Adresse :</strong><br />
              XCANNES LLC<br />
              131 Continental Dr, Suite 305<br />
              Newark, Delaware 19713<br />
              United States
            </p>
            <p>
              Pour toute question relative à la protection des données personnelles ou pour exercer vos droits :<br />
              <a href="mailto:contact@diaglink.com">contact@diaglink.com</a>
            </p>
          </section>
          <section>
            <h2>2. Données pouvant être collectées</h2>
            <p>Selon l’utilisation du service, DiagLink peut traiter les catégories de données suivantes :</p>
            <ul>
              {collectedData.map(item => <li key={item.label}><strong>{item.label}</strong> {item.description}</li>)}
            </ul>
            <p className={styles.listFollowup}>Les données complètes de carte bancaire ne sont pas stockées par DiagLink. Elles sont traitées directement par <strong>Stripe</strong> dans le cadre du paiement.</p>
            <p>Les informations signalées comme obligatoires lors de la création ou de l’utilisation du compte sont nécessaires à la fourniture du service. Leur absence peut empêcher la création du compte ou l’accès à certaines fonctionnalités de DiagLink.</p>
          </section>
          <section className={styles.documentSection}><span className={styles.documentIcon}><DocumentLock24Regular /></span><div><p className={styles.sectionLabel}>3. Documentation technique</p><h2>Vos documents techniques restent vos documents.</h2><p>Les manuels, notices, schémas, vues éclatées et autres documents transmis à DiagLink restent la propriété de leurs titulaires. Leur transmission à DiagLink n’entraîne aucun transfert de propriété ou de droits de propriété intellectuelle.</p><p>Ils sont traités uniquement dans le cadre du service DiagLink associé aux machines concernées et ne sont pas rendus accessibles aux autres clients.</p></div></section>
          <section>
            <h2>4. Utilisation des données</h2>
            <p>Les données personnelles traitées par DiagLink sont utilisées uniquement pour les finalités nécessaires au fonctionnement et à la gestion du service.</p>
            <ul>
              {purposes.map(item => <li key={item.purpose}><strong>{item.purpose}</strong> — {item.basis}</li>)}
            </ul>
          </section>
          <section>
            <h2>5. Intelligence artificielle</h2>
            <p>Certaines fonctionnalités de DiagLink reposent sur des systèmes d’intelligence artificielle fournis via Microsoft Foundry.</p>
            <p>Lorsqu’un utilisateur interroge l’assistant technique, sa demande ainsi que, lorsque cela est nécessaire, des extraits pertinents de la documentation technique associée à la machine peuvent être transmis au modèle d’intelligence artificielle afin de générer une réponse contextualisée.</p>
            <p>Les données transmises aux modèles d’intelligence artificielle ne sont pas utilisées par DiagLink pour entraîner des modèles. Lorsqu’un modèle est fourni via Microsoft Foundry, le traitement des données est soumis aux conditions de confidentialité et de traitement des données du fournisseur du modèle et de Microsoft.</p>
            <p>Selon le modèle utilisé dans Microsoft Foundry, le traitement peut être réalisé sur l’infrastructure Microsoft Azure ou sur l’infrastructure du fournisseur du modèle.</p>
            <p>Les réponses générées automatiquement peuvent comporter des erreurs ou des imprécisions. Elles constituent une aide au diagnostic et ne remplacent pas les vérifications, procédures de sécurité et décisions du technicien.</p>
            <p>DiagLink n’utilise pas l’intelligence artificielle pour prendre, de manière entièrement automatisée, des décisions produisant des effets juridiques à l’égard des utilisateurs.</p>
          </section>
          <section>
            <h2>6. Prestataires techniques</h2>
            <p>Pour assurer le fonctionnement de DiagLink, certains prestataires techniques peuvent être amenés à traiter des données dans la mesure nécessaire à la fourniture de leurs services.</p>
            <p>DiagLink s’appuie notamment sur :</p>
            <ul>
              <li><strong>Microsoft Azure et Microsoft Foundry</strong>, pour l’hébergement, le stockage, les services techniques et les fonctionnalités d’intelligence artificielle ;</li>
              <li><strong>les fournisseurs de modèles d’intelligence artificielle accessibles via Microsoft Foundry</strong>, lorsque leur intervention est nécessaire au traitement d’une demande ;</li>
              <li><strong>Stripe</strong>, pour la gestion des paiements, abonnements et opérations de facturation.</li>
            </ul>
            <p className={styles.listFollowup}>L’accès aux données est limité aux informations nécessaires au service concerné. Les relations avec les prestataires traitant des données pour le compte de DiagLink sont encadrées contractuellement conformément aux exigences applicables en matière de protection des données.</p>
            <p>Certains prestataires peuvent traiter des données en dehors de l’Espace économique européen. Lorsque cela est applicable, ces transferts sont encadrés par les mécanismes et garanties prévus par la réglementation en matière de protection des données.</p>
          </section>
          <section>
            <h2>7. Conservation des données</h2>
            <p>Les données personnelles sont conservées pendant la durée nécessaire à la fourniture du service DiagLink et à la gestion de la relation avec le client ou l’utilisateur.</p>
            <p>Les données liées au compte utilisateur, à l’accès aux machines et à l’utilisation du service sont conservées pendant la durée d’utilisation du service, puis supprimées ou anonymisées lorsqu’elles ne sont plus nécessaires, sous réserve des obligations légales applicables.</p>
            <p>Les conversations avec l’assistant technique et les données techniques associées sont conservées pendant une durée limitée nécessaire au fonctionnement, au suivi et à la sécurité du service.</p>
            <p>Les documents techniques associés aux machines sont conservés pendant la durée nécessaire à la fourniture du service correspondant, sauf demande de suppression ou obligation contraire.</p>
            <p>Les informations et documents de facturation devant être conservés pour répondre à des obligations légales peuvent être archivés pendant la durée prévue par la réglementation applicable, notamment jusqu’à <strong>10 ans</strong> pour certains documents comptables.</p>
            <p>À l’issue des durées applicables, les données sont supprimées, anonymisées ou archivées lorsque leur conservation reste nécessaire pour respecter une obligation légale ou assurer la défense des droits de DiagLink.</p>
          </section>
          <section>
            <h2>8. Sécurité et contrôle des accès</h2>
            <p>DiagLink met en œuvre des mesures techniques et organisationnelles destinées à protéger les données contre l’accès non autorisé, la perte, l’altération ou la divulgation.</p>
            <p>L’accès aux données est limité aux utilisateurs et services autorisés, en fonction de leurs droits et des besoins nécessaires au fonctionnement de DiagLink.</p>
            <p>Les utilisateurs n’ont accès qu’aux entreprises, machines, documents et fonctionnalités pour lesquels des droits leur ont été attribués.</p>
            <p>Des mécanismes d’authentification, de contrôle des accès et de journalisation technique peuvent être utilisés afin de contribuer à la sécurité du service et à la détection d’éventuels incidents.</p>
            <p>DiagLink veille également à limiter l’accès aux données par ses prestataires techniques aux seules informations nécessaires à la fourniture de leurs services.</p>
          </section>
          <section>
            <h2>9. Droits des utilisateurs</h2>
            <p>Conformément à la réglementation applicable en matière de protection des données, les personnes concernées peuvent disposer, selon les traitements concernés, des droits suivants :</p>
            <ul>
              {rights.map(item => <li key={item.label}><strong>{item.label}</strong> {item.description}</li>)}
            </ul>
            <p className={styles.listFollowup}>Pour exercer ces droits ou poser toute question relative à leurs données personnelles, les utilisateurs peuvent contacter DiagLink à l’adresse suivante :</p>
            <p><strong><a href="mailto:contact@diaglink.com">contact@diaglink.com</a></strong></p>
            <p>DiagLink pourra demander des informations complémentaires lorsque cela est nécessaire pour vérifier l’identité du demandeur et éviter qu’une demande ne permette l’accès aux données d’un tiers.</p>
            <p>Les personnes concernées disposent également du droit d’introduire une réclamation auprès de l’autorité de protection des données compétente. Pour les personnes situées en France, il est notamment possible de saisir la <strong>Commission nationale de l’informatique et des libertés (CNIL)</strong>.</p>
          </section>
          <section>
            <h2>10. Cookies et stockage local</h2>
            <p>DiagLink utilise certaines technologies de stockage dans le navigateur nécessaires au fonctionnement du service.</p>
            <p>Le stockage local et le stockage de session peuvent notamment être utilisés pour conserver des préférences d’interface, des informations temporaires liées à l’authentification, la machine actuellement sélectionnée et certaines informations nécessaires au bon fonctionnement de l’application.</p>
            <p>Ces éléments sont utilisés à des fins fonctionnelles et techniques et ne sont pas utilisés à des fins publicitaires.</p>
            <p>DiagLink peut également utiliser des outils de télémétrie et de mesure technique, notamment <strong>Microsoft Application Insights</strong>, afin de détecter les erreurs, surveiller les performances et améliorer la fiabilité du service.</p>
            <p>Lorsque l’utilisation d’un cookie ou d’un autre traceur nécessite le consentement de l’utilisateur conformément à la réglementation applicable, celui-ci est recueilli avant son dépôt ou son utilisation.</p>
          </section>
          <section>
            <h2>11. Modification de la politique</h2>
            <p>DiagLink peut modifier la présente politique de confidentialité afin de tenir compte des évolutions du service, de ses pratiques ou des exigences légales et réglementaires applicables.</p>
            <p>En cas de modification importante concernant le traitement des données personnelles, les utilisateurs pourront être informés par un moyen approprié.</p>
            <p>La version applicable est celle publiée sur le site DiagLink à la date indiquée ci-dessous.</p>
          </section>
          <p className={styles.updated}>Dernière mise à jour : septembre 2026</p>
        </article>
      </main>
      <PublicFooter loginTarget={loginTarget} />
    </div>
  );
}
