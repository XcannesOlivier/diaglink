import { Link } from 'react-router-dom';
import { PublicHeader } from '../../components/marketing/PublicHeader';
import { PublicFooter } from '../../components/marketing/ClosingSections';
import landingStyles from '../landing/LandingPage.module.css';
import legalStyles from '../privacy/PrivacyPage.module.css';

// TODO(juridique): remplacer les valeurs null ci-dessous par les informations légales validées.
// Les champs non renseignés ne sont volontairement pas affichés sur la page publique.
const publisherDetails: Array<{ label: string; value: string | null }> = [
  { label: 'Forme juridique / nom de l’entreprise ou identité de l’entrepreneur', value: null },
  { label: 'Adresse du siège ou adresse professionnelle', value: null },
  { label: 'Numéro SIREN / SIRET', value: null },
  { label: 'Numéro RCS, si applicable', value: null },
  { label: 'Numéro de TVA intracommunautaire, si applicable', value: null },
  { label: 'Adresse e-mail de contact', value: null },
  { label: 'Directeur de la publication', value: null },
];

export function LegalNoticePage({ isAuthenticated }: { isAuthenticated: boolean }) {
  const loginTarget = isAuthenticated ? '/app' : '/login';
  const completedPublisherDetails = publisherDetails.filter(
    (detail): detail is { label: string; value: string } => detail.value !== null,
  );

  return (
    <div className={landingStyles.page}>
      <PublicHeader loginTarget={loginTarget} isAuthenticated={isAuthenticated} landingPath="/" />
      <main className={legalStyles.main}>
        <article className={legalStyles.article}>
          <header className={legalStyles.intro}>
            <p className={landingStyles.eyebrow}>Informations légales</p>
            <h1>Mentions légales</h1>
          </header>

          <section>
            <h2>1. Éditeur du site</h2>
            <p><strong>DiagLink</strong></p>
            {completedPublisherDetails.length > 0 && (
              <ul>
                {completedPublisherDetails.map(detail => (
                  <li key={detail.label}><strong>{detail.label} :</strong> {detail.value}</li>
                ))}
              </ul>
            )}
          </section>
          <section>
            <h2>2. Hébergement</h2>
            <p>Le service DiagLink est hébergé sur l’infrastructure Microsoft Azure.</p>
          </section>
          <section>
            <h2>3. Propriété intellectuelle</h2>
            <p>Le site DiagLink, son identité visuelle, ses textes, ses interfaces et les éléments propres au service sont protégés par les règles applicables en matière de propriété intellectuelle.</p>
            <p>Les documentations techniques importées par les clients restent la propriété de leurs détenteurs respectifs.</p>
          </section>
          <section>
            <h2>4. Service DiagLink</h2>
            <p>DiagLink est un service permettant d’exploiter et d’interroger la documentation technique associée à des machines afin d’aider les utilisateurs à retrouver les informations pertinentes. Les réponses de l’assistant constituent une aide et ne garantissent pas un diagnostic.</p>
          </section>
          <section>
            <h2>5. Responsabilité</h2>
            <p>Les informations fournies par DiagLink constituent une aide à la recherche et au diagnostic technique. Les opérations de maintenance, mesures, contrôles et interventions restent sous la responsabilité des professionnels qui les réalisent et doivent respecter les procédures de sécurité, les instructions du constructeur et la réglementation applicable.</p>
          </section>
          <section>
            <h2>6. Données personnelles</h2>
            <p>Les informations relatives au traitement des données personnelles sont présentées dans notre <Link to="/confidentialite">Politique de confidentialité</Link>.</p>
          </section>
          <section>
            <h2>7. Contact</h2>
            <p>Pour toute question concernant le site ou le service DiagLink, vous pouvez nous contacter depuis notre page <Link to="/contact">Contact</Link>.</p>
          </section>
          <p className={legalStyles.updated}>Dernière mise à jour : septembre 2026</p>
        </article>
      </main>
      <PublicFooter loginTarget={loginTarget} />
    </div>
  );
}
