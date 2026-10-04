import { Link } from 'react-router-dom';
import { PublicHeader } from '../../components/marketing/PublicHeader';
import { PublicFooter } from '../../components/marketing/ClosingSections';
import { APP_LOGIN_URL } from '../../config/origins';
import landingStyles from '../landing/LandingPage.module.css';
import legalStyles from '../privacy/PrivacyPage.module.css';

export function LegalNoticePage({ isAuthenticated }: { isAuthenticated: boolean }) {
  const loginTarget = APP_LOGIN_URL;

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
            <p>Le site et le service <strong>DiagLink</strong> sont édités par :</p>
            <p>
              <strong>XCANNES LLC</strong><br />
              Limited Liability Company enregistrée dans l’État du Delaware, États-Unis<br />
              <strong>Delaware File Number : 10157026</strong><br />
              131 Continental Dr, Suite 305<br />
              Newark, Delaware 19713<br />
              United States
            </p>
            <p><strong>E-mail :</strong> <a href="mailto:contact@diaglink.com">contact@diaglink.com</a></p>
            <p><strong>Téléphone :</strong> <a href="tel:+19179708191">+1 917 970 8191</a></p>
            <p><strong>Directeur de la publication :</strong> Olivier Desruelle</p>
          </section>
          <section>
            <h2>2. Hébergement</h2>
            <p>Le service DiagLink est hébergé sur l’infrastructure <strong>Microsoft Azure</strong>.</p>
            <p>
              <strong>Microsoft France SAS</strong><br />
              37-45 Quai du Président Roosevelt<br />
              92130 Issy-les-Moulineaux<br />
              France
            </p>
            <p><strong>Téléphone :</strong> 09 70 01 90 90</p>
          </section>
          <section>
            <h2>3. Propriété intellectuelle</h2>
            <p>Le site DiagLink, son identité visuelle, ses textes, ses interfaces, son logiciel et les éléments propres au service sont protégés par les règles applicables en matière de propriété intellectuelle.</p>
            <p>Toute reproduction, représentation ou utilisation non autorisée de ces éléments est susceptible de constituer une atteinte aux droits de leurs titulaires.</p>
            <p>Les documentations techniques importées par les clients restent la propriété de leurs détenteurs respectifs.</p>
          </section>
          <section>
            <h2>4. Service DiagLink</h2>
            <p>DiagLink est un service permettant d’exploiter et d’interroger la documentation technique associée à des machines afin d’aider les utilisateurs à retrouver les informations pertinentes.</p>
            <p>Les réponses générées par l’assistant constituent une aide au diagnostic technique et ne garantissent pas l’exactitude d’un diagnostic.</p>
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
            <p>Pour toute question concernant le site ou le service DiagLink, vous pouvez nous contacter à l’adresse suivante :</p>
            <p><strong><a href="mailto:contact@diaglink.com">contact@diaglink.com</a></strong></p>
            <p>Vous pouvez également utiliser notre <strong><Link to="/contact">page Contact</Link></strong>.</p>
          </section>
          <p className={legalStyles.updated}>Dernière mise à jour : septembre 2026</p>
        </article>
      </main>
      <PublicFooter loginTarget={loginTarget} />
    </div>
  );
}
