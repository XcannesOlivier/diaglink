# Recharge CompanyWallet — MVP Stripe test

## Contrat financier

Une recharge est en EUR, à partir de 10 €, avec au plus deux décimales. Les montants
proposés sont 10 / 20 / 50 / 100 / 200 €. La borne technique Stripe est de huit
chiffres en centimes (999 999,99 €). Les montants hors format sont rejetés avant
création de Customer ou Checkout ; aucun arrondi implicite du montant demandé.

**1 € effectivement payé = 1 € de CommercialCreditAmount.** Pas de coefficient ×3
sur la recharge. Le débit IA existant conserve son propre coefficient.
Un wallet absent est créé automatiquement **uniquement après paiement confirmé**.
Un wallet non EUR ou incohérent nécessite une réconciliation sans crédit.

## Opération et paiements

`StripeWalletTopUps` stocke le requestId UUID, l’entreprise, Customer, montant en
centimes, devise et URL de retour figés, ainsi que session, PaymentIntent, événement,
dates de confirmation et référence ledger.

`Reserved → PaymentCreated → AwaitingPayment → PaymentConfirmed → WalletCredited → Completed`.

Une Checkout Session `mode=payment`, carte uniquement, sans remise ni taxe ajoutée,
est créée avec une clé `diaglink:wallet-topup:{operationId}:checkout` stable. Les
métadonnées entreprise/opération sont attachées à la session ET au PaymentIntent.
Au-delà de 23 heures, une création dont l’identité n’a pas pu être enregistrée ne
crée pas une nouvelle session : ReconciliationRequired. Un webhook peut toutefois
récupérer cette opération par ses métadonnées signées et la session relue sur Stripe.
Une session déjà enregistrée et les opérations payées restent reprenables après 23 h.

Le serveur relit la session avec PaymentIntent/latest_charge expansés. Il vérifie
mode test, Customer, entreprise/opération, EUR, subtotal/total, paid/complete,
PaymentIntent succeeded et amount_received exacts, charge capturée/payée et non
remboursée. Ni le retour navigateur ni le contenu du webhook seul ne créditent.
Les événements non payés renvoient AwaitingPayment/503 pour permettre une reprise.
Une incohérence renvoie ReconciliationRequired/200 et est journalisée sans correction.

La preuve (PaymentIntent, date de charge Stripe, premier eventId) est persistée
avant crédit. Puis une transaction sérialisable avec verrou du wallet, compatible
avec CompanyWalletDebitService, effectue ensemble :

- création éventuelle du wallet EUR ;
- incrément exact de Balance ;
- ajout d’un ledger `TopUp / CompanyWallet`, montant commercial positif,
  `BalanceAfter`, `Currency=EUR`, `ExternalEventId` ; coût IA et références machine nuls ;
- checkpoint WalletCredited et LedgerEntryId.

LedgerEntryId est déterministe (ID de l’opération). Unicité du requestId, de la
session, du PaymentIntent, de l’événement et du ledger ; l’index ExternalEventId
du ledger existant demeure utilisé. Un nouvel événement pour le même paiement
ne crédite pas de nouveau. Un crash avant commit annule balance+ledger+checkpoint ;
après commit, la reprise ne fait que compléter l’opération. Aucun appel Stripe
n’est dans la transaction ou dans sa stratégie EF de retry.

Les entrées TopUp sont immuables via EF et un trigger SQL UPDATE/DELETE, sans
modifier les anciennes lignes. Le mapping CreditLedger désactive SQL OUTPUT
pour la compatibilité trigger. Les tests SQLite vérifient l’immutabilité EF ; le
trigger et les verrous SQL Server restent à vérifier sur la base cible.

## Routes et configuration

- `GET /api/companies/{companyId}/stripe/wallet-topups` : solde et opérations,
  sans création ni contact Stripe.
- `POST /api/companies/{companyId}/stripe/wallet-topups` : `{requestId, amount,
  currency:"EUR"}`. SuperAdminOnly comme le GET. Réutiliser le même requestId
  et le même montant après toute réponse incertaine ; un UUID différent désigne
  une nouvelle recharge indépendante.
- `POST /api/stripe/webhooks/wallet-topups` : signature Stripe obligatoire sur
  corps brut, tolérance SDK 300 secondes et vérification version API.
  Événements : `checkout.session.completed`, `checkout.session.async_payment_succeeded`.
  Mode live/Connect refusé. Aucun secret/payload/provider exception brut dans les logs.

Conserver la configuration Stripe test existante (`STRIPE_ENABLED`, SecretKey test,
`STRIPE_PRICE_ID` pour la validation commune ; aucun tarif abonnement n’est lu
pour calculer la recharge). Ajouter :

- `STRIPE_TOPUP_RETURN_URL` : URL serveur configurée HTTPS, ou HTTP localhost pour
  le test local. Elle est figée dans l’opération ; jamais fournie par le navigateur.
- `STRIPE_TOPUP_WEBHOOK_SECRET` : secret propre à cette destination signée.

Migration créée : **20260911143401_AddStripeWalletTopUps**. Elle crée la table,
checks, FK et index uniques filtrés, et le trigger d’immutabilité TopUp.
Elle n’est pas appliquée par cette implémentation et n’insère aucune donnée.

## Interface et test réel à effectuer

Administration DiagLink → Stripe test → entreprise → Recharge wallet. Le bloc est
aussi accessible dans le détail entreprise. Solde, presets, montant libre,
Checkout externe, état et références sont visibles. Le navigateur conserve le
requestId avant envoi dans localStorage pour reprendre après timeout/rechargement.
Il ne modifie jamais optimistement le solde. Actualiser fait seulement un GET.

Après application séparément autorisée de la migration sur l’environnement de test,
configurer la destination webhook/version API compatible SDK et ses secrets.
Créer une recharge, payer dans Checkout avec une carte Stripe test, vérifier
Completed, un seul ledger et la balance exacte. Redélivrer l’événement, rejouer
l’opération, tester SCA/échec de paiement et concurrence SQL Server avec débit IA.
Pour un Checkout expiré, ne pas recréer silencieusement le paiement de la même
opération ; une nouvelle recharge utilise un nouvel UUID explicite.

Aucun remboursement, avoir, annulation comptable, prélèvement live ou migration
Azure pendant l’implémentation. Tests uniquement sur SQLite en mémoire,
transport Stripe simulé et événements signés locaux.

Référence : [Stripe — Checkout fulfillment](https://docs.stripe.com/checkout/fulfillment).
