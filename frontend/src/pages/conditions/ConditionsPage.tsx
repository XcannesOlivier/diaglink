import { useEffect } from 'react';
import { Link } from 'react-router-dom';
import { PublicHeader } from '../../components/marketing/PublicHeader';
import { PublicFooter } from '../../components/marketing/ClosingSections';
import { APP_LOGIN_URL } from '../../config/origins';
import landingStyles from '../landing/LandingPage.module.css';
import legalStyles from '../privacy/PrivacyPage.module.css';
import styles from './ConditionsPage.module.css';

export function ConditionsContent({ compact = false, showTitle = true }: { compact?: boolean; showTitle?: boolean }) {
  return (
    <>
          <header className={`${legalStyles.intro}${compact ? ` ${styles.compactIntro}` : ''}`}>
            {showTitle && <>
              <p className={landingStyles.eyebrow}>Clients professionnels</p>
              <h1>Conditions générales de service</h1>
            </>}
            <p className={styles.introText}>Les présentes Conditions générales de service régissent la souscription et l’utilisation du service <strong>DiagLink</strong>, exploité par <strong>XCANNES LLC</strong>.</p>
            <div className={styles.updated}>Dernière mise à jour : septembre 2026</div>
          </header>

          <section>
            <h2>1. Objet et clients concernés</h2>
            <p>DiagLink est un service destiné exclusivement aux <strong>professionnels</strong>.</p>
            <p>Il permet aux entreprises et à leurs utilisateurs autorisés d’accéder à la documentation technique associée à leurs machines et d’utiliser un assistant technique reposant notamment sur des technologies d’intelligence artificielle.</p>
            <p>Toute souscription à DiagLink implique l’acceptation des présentes Conditions générales de service.</p>
            <p>Des conditions particulières peuvent être convenues par écrit avec un client. Lorsqu’elles existent, elles prévalent sur les présentes conditions pour les éléments concernés.</p>
          </section>

          <section>
            <h2>2. Service DiagLink</h2>
            <p>DiagLink permet notamment :</p>
            <ul>
              <li>d’associer de la documentation technique à une machine ;</li>
              <li>de rechercher des informations dans cette documentation ;</li>
              <li>d’interroger un assistant technique ;</li>
              <li>d’accompagner les utilisateurs dans leurs recherches et démarches de diagnostic.</li>
            </ul>
            <p className={legalStyles.listFollowup}>L’utilisation du service nécessite qu’une machine soit active dans le compte du client.</p>
            <p>La mise en service d’une nouvelle machine peut nécessiter une phase préalable de préparation et de traitement de sa documentation technique.</p>
          </section>

          <section>
            <h2>3. Tarifs et crédit agent</h2>
            <p>L’abonnement DiagLink est facturé <strong>29,90 € HT par mois et par machine active</strong>.</p>
            <p>Les abonnements sont renouvelés le <strong>1er de chaque mois</strong>.</p>
            <p>Lorsqu’une première machine ou une machine supplémentaire est activée en cours de mois, son premier abonnement est facturé <strong>au prorata du nombre de jours restant jusqu’au prochain 1er du mois</strong>.</p>
            <p>À compter du 1er du mois suivant, la machine est facturée au tarif mensuel normal en vigueur.</p>
            <p>Chaque abonnement comprend un <strong>crédit agent mensuel inclus</strong> permettant l’utilisation de l’assistant technique.</p>
            <p>Lorsqu’une machine est activée en cours de mois, le prix de son abonnement est calculé au prorata, mais <strong>le crédit agent inclus est attribué intégralement dès son activation</strong>, quelle que soit la date de souscription.</p>
            <p>Le solde du crédit inclus peut être consulté dans l’application, notamment sous forme de pourcentage restant.</p>
            <p>Le crédit inclus est renouvelé à chaque nouvelle période mensuelle et la partie non utilisée n’est pas reportée sur la période suivante.</p>
            <p>Des crédits d’utilisation supplémentaires peuvent être achetés selon les tarifs affichés dans DiagLink avant la validation de l’achat. Ces crédits supplémentaires restent disponibles tant que le compte de l’entreprise existe.</p>
            <p>Les crédits constituent uniquement des droits d’utilisation du service. Ils ne constituent pas une somme d’argent, ne peuvent pas être convertis en espèces et ne sont pas remboursables, sauf obligation légale contraire.</p>
          </section>

          <section>
            <h2>4. Préparation de la documentation</h2>
            <p>La préparation documentaire initiale d’une nouvelle machine est facturée :</p>
            <p><strong>99,90 € HT jusqu’à 400 pages de documentation</strong>, puis<br /><strong>0,27 € HT par page supplémentaire au-delà de 400 pages</strong>.</p>
            <p>Le nombre de pages et le montant correspondant sont indiqués au client avant la validation de la demande lorsque ces informations peuvent être déterminées à ce stade.</p>
            <p>La préparation documentaire permet à DiagLink de traiter et d’organiser les documents nécessaires au fonctionnement de l’assistant technique associé à la machine.</p>
          </section>

          <section>
            <h2>5. Paiement et facturation</h2>
            <p>Les paiements, abonnements et recharges sont notamment traités par <strong>Stripe</strong>.</p>
            <p>Les abonnements sont payables d’avance.</p>
            <p>Lorsqu’un paiement de renouvellement prévu le 1er du mois échoue ou n’est pas effectué, <strong>l’abonnement concerné n’est pas renouvelé</strong> et l’accès au service correspondant est suspendu à compter de la date prévue de renouvellement.</p>
            <p>DiagLink ne facture donc pas une nouvelle période mensuelle lorsque son renouvellement n’a pas été payé.</p>
            <p>La réactivation du service peut nécessiter la régularisation du paiement et le renouvellement de l’abonnement.</p>
            <p>Aucun escompte n’est accordé pour paiement anticipé.</p>
            <p>Dans le cas exceptionnel où une somme déjà facturée et exigible resterait impayée après son échéance, les pénalités et indemnités de recouvrement prévues par la réglementation applicable entre professionnels pourront s’appliquer.</p>
          </section>

          <section>
            <h2>6. Durée, renouvellement et résiliation</h2>
            <p>Les abonnements fonctionnent par périodes mensuelles allant du <strong>1er au dernier jour du mois</strong>.</p>
            <p>Lorsqu’une machine est activée en cours de mois, sa première période commence à sa date d’activation et se termine à la fin du mois correspondant.</p>
            <p>Les machines actives sont ensuite renouvelées automatiquement le <strong>1er de chaque mois</strong>, sous réserve du paiement.</p>
            <p>Le client peut demander l’arrêt d’un abonnement à tout moment. Celui-ci reste actif jusqu’à la fin de la période déjà payée et n’est ensuite pas renouvelé.</p>
            <p>Les sommes correspondant à une période déjà commencée et payée ne sont pas remboursées, sauf erreur de facturation imputable à DiagLink ou disposition légale contraire.</p>
            <p>En cas d’absence ou d’échec du paiement du renouvellement, l’accès à la machine concernée est suspendu à la date prévue de renouvellement.</p>
            <p>Les crédits supplémentaires éventuellement disponibles restent attachés au compte de l’entreprise, mais ne permettent pas d’utiliser une machine dont l’abonnement n’est plus actif.</p>
          </section>

          <section>
            <h2>7. Documents et propriété intellectuelle</h2>
            <p>Les manuels, notices, schémas, vues éclatées et autres documents transmis à DiagLink restent la propriété de leurs titulaires respectifs.</p>
            <p>Leur transmission à DiagLink n’entraîne aucun transfert de propriété ou de droits de propriété intellectuelle.</p>
            <p>Le client déclare disposer des droits ou autorisations nécessaires pour transmettre et utiliser ces documents dans le cadre du service.</p>
            <p>Les documents d’un client ne sont pas rendus accessibles aux autres clients de DiagLink.</p>
            <p>Le logiciel DiagLink, son interface, son identité visuelle, ses fonctionnalités et les éléments propres au service restent la propriété de XCANNES LLC ou de leurs titulaires respectifs.</p>
          </section>

          <section>
            <h2>8. Assistant technique et intelligence artificielle</h2>
            <p>DiagLink utilise notamment des systèmes d’intelligence artificielle afin de rechercher, analyser et présenter des informations issues de la documentation technique associée aux machines.</p>
            <p>Les réponses générées peuvent comporter des erreurs, omissions ou imprécisions.</p>
            <p>DiagLink constitue une <strong>aide à la recherche et au diagnostic technique</strong> et ne remplace pas les contrôles, les procédures de sécurité ou les décisions d’un professionnel qualifié.</p>
            <p>Avant toute intervention, l’utilisateur doit vérifier les informations pertinentes et respecter les instructions du constructeur, les procédures de sécurité applicables et la réglementation en vigueur.</p>
            <p>Les opérations de maintenance, mesures, contrôles et interventions restent sous la responsabilité des professionnels qui les réalisent.</p>
          </section>

          <section>
            <h2>9. Obligations du client et disponibilité du service</h2>
            <p>Le client est responsable des utilisateurs auxquels il autorise l’accès à DiagLink et des droits qui leur sont attribués.</p>
            <p>Le client s’engage notamment à :</p>
            <ul>
              <li>fournir des informations exactes ;</li>
              <li>protéger l’accès à ses comptes ;</li>
              <li>utiliser DiagLink dans un cadre professionnel et licite ;</li>
              <li>transmettre uniquement des documents qu’il est autorisé à utiliser ;</li>
              <li>ne pas tenter de contourner les dispositifs de sécurité ou les limitations du service ;</li>
              <li>respecter les procédures de sécurité applicables aux machines et aux interventions.</li>
            </ul>
            <p className={legalStyles.listFollowup}>DiagLink met en œuvre des moyens raisonnables afin d’assurer la disponibilité et le bon fonctionnement du service.</p>
            <p>Des interruptions temporaires peuvent toutefois survenir en raison notamment d’une maintenance, d’une mise à jour, d’un incident technique ou d’une interruption affectant un prestataire nécessaire au fonctionnement du service.</p>
            <p>DiagLink ne garantit pas un fonctionnement permanent et sans interruption.</p>
          </section>

          <section>
            <h2>10. Responsabilité</h2>
            <p>DiagLink fournit un outil d’assistance technique et de recherche documentaire.</p>
            <p>Dans les limites permises par la réglementation applicable, XCANNES LLC ne peut être tenue responsable que des dommages directs résultant d’un manquement qui lui est imputable.</p>
            <p>DiagLink ne peut notamment pas garantir qu’une réponse générée par l’assistant aboutira à un diagnostic correct ou complet.</p>
            <p>Le client et ses utilisateurs restent responsables de leurs décisions, de leurs vérifications et des interventions effectuées sur les machines.</p>
            <p>Aucune disposition des présentes conditions n’a pour objet d’exclure ou de limiter une responsabilité qui ne pourrait légalement l’être.</p>
          </section>

          <section>
            <h2>11. Données personnelles et confidentialité</h2>
            <p>Les règles relatives au traitement des données personnelles sont détaillées dans la <strong><Link to="/confidentialite">Politique de confidentialité de DiagLink</Link></strong>.</p>
            <p>Les documents techniques transmis par un client sont utilisés dans le cadre de la fourniture du service correspondant et ne sont pas rendus accessibles aux autres clients.</p>
            <p>Les prestataires techniques nécessaires au fonctionnement du service peuvent traiter certaines informations dans les conditions décrites dans la Politique de confidentialité.</p>
          </section>

          <section>
            <h2>12. Suspension du service</h2>
            <p>DiagLink peut suspendre tout ou partie de l’accès au service notamment en cas :</p>
            <ul>
              <li>d’absence de paiement d’un renouvellement ;</li>
              <li>d’utilisation frauduleuse ou manifestement abusive ;</li>
              <li>d’atteinte ou tentative d’atteinte à la sécurité du service ;</li>
              <li>d’utilisation contraire aux présentes conditions ;</li>
              <li>de nécessité technique ou de sécurité urgente.</li>
            </ul>
            <p className={legalStyles.listFollowup}>Lorsque la situation peut être régularisée, l’accès pourra être rétabli après sa résolution.</p>
          </section>

          <section>
            <h2>13. Modification des conditions</h2>
            <p>DiagLink peut faire évoluer les présentes conditions afin de tenir compte notamment des évolutions du service, de ses tarifs ou de la réglementation.</p>
            <p>Lorsqu’une modification importante concerne un abonnement en cours, le client en est informé par un moyen approprié avant son application.</p>
            <p>Toute modification du prix de l’abonnement applicable aux périodes futures est communiquée au client avant son entrée en vigueur afin qu’il puisse, s’il le souhaite, mettre fin à son abonnement avant la nouvelle période concernée.</p>
            <p>La version applicable est celle acceptée par le client et publiée par DiagLink pour la période concernée.</p>
          </section>

          <section>
            <h2>14. Litiges et contact</h2>
            <p>En cas de difficulté ou de différend, le client est invité à contacter DiagLink afin de rechercher en priorité une solution amiable.</p>
            <p>À défaut de résolution amiable, les règles légales applicables déterminent le droit et les juridictions compétentes.</p>
            <p>Le service est exploité par :</p>
            <p>
              <strong>XCANNES LLC</strong><br />
              Limited Liability Company enregistrée dans l’État du Delaware, États-Unis<br />
              <strong>Delaware File Number : 10157026</strong><br />
              131 Continental Dr, Suite 305<br />
              Newark, Delaware 19713<br />
              United States
            </p>
            <p><strong>E-mail :</strong> <a href="mailto:contact@diaglink.com">contact@diaglink.com</a><br /><strong>Téléphone :</strong> <a href="tel:+19179708191">+1 917 970 8191</a></p>
            <p><strong>Directeur de la publication : Olivier Desruelle</strong></p>
          </section>
    </>
  );
}

export function ConditionsPage({ isAuthenticated }: { isAuthenticated: boolean }) {
  const loginTarget = APP_LOGIN_URL;

  useEffect(() => {
    window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
  }, []);

  return (
    <div className={landingStyles.page}>
      <PublicHeader loginTarget={loginTarget} isAuthenticated={isAuthenticated} landingPath="/" />
      <main className={legalStyles.main}>
        <article className={legalStyles.article}>
          <ConditionsContent />
        </article>
      </main>
      <PublicFooter loginTarget={loginTarget} />
    </div>
  );
}
