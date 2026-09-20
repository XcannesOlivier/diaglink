# MVP Stripe test — parcours de validation

Le bloc **Administration DiagLink → Stripe test** permet de choisir une entreprise,
consulter BillingAccount/Customer/Subscription/cycle et machines actives, créer ou
récupérer le Customer puis la Subscription et suivre les ajouts de machines.
Le détail Entreprises conserve le même bloc. Toutes les routes administratives
exigent `SuperAdminOnly`. Les commandes et le webhook refusent les clés live,
même si `STRIPE_ALLOW_LIVE` est activé ailleurs.

## Préparation de l’environnement de test

- Configurer `STRIPE_ENABLED=true`, `STRIPE_ALLOW_LIVE=false`, une clé secrète test
  dans `STRIPE_SECRET_KEY`, le prix existant dans `STRIPE_PRICE_ID`, et le secret
  de signature dans `STRIPE_WEBHOOK_SECRET`. Aucun secret dans le frontend.
- Le prix doit être actif, EUR, mensuel, 2990 centimes HT, conforme à la validation
  existante du gateway. Pas de création dynamique de Product/Price.
- Utiliser une base de test contenant entreprises/machines et les migrations
  existantes. La migration déjà créée
  `20260911124704_AddStripeMachineAdditionPaymentEvent` doit être appliquée à la
  base cible avant les tests du webhook. Aucune nouvelle migration pour ce MVP.
  L’implémentation n’applique aucune migration et ne contacte pas Azure.
- Exposer le webhook existant à la destination Stripe test ou via le forwarding
  local Stripe. Utiliser son secret de signature propre et la version API
  compatible avec le SDK Stripe.net installé. Événement : `invoice.payment_succeeded`.

## Parcours réel en Stripe test

1. Choisir une entreprise active ayant des machines actives. Créer/récupérer le
   Customer. Dans le Dashboard Stripe test, lui associer un moyen de paiement de
   test par défaut. Aucun checkout ni saisie de carte dans DiagLink.
2. Créer/récupérer la Subscription. La quantité initiale est le nombre de machines
   actives. Si la facture initiale attend un paiement, le terminer côté Stripe
   test puis récupérer à nouveau l’abonnement pour synchroniser son statut/cycle.
   Cette facture initiale n’est pas une opération StripeMachineAddition.
3. Après la création de l’abonnement, rendre disponible une nouvelle machine active
   par le processus machine existant. Ne pas réutiliser une machine déjà couverte
   par une période. Le sélecteur exclut les machines avec historique ou opération ;
   le service vérifie aussi que la quantité Stripe ne couvre pas déjà cet ajout.
4. Cliquer **Lancer l’ajout Stripe**. Le serveur fixe la date de l’opération au
   premier lancement ; les reprises réutilisent la date persistée, jamais une
   nouvelle date du navigateur. Quantité +1 sans prorata Stripe ni déplacement de
   renouvellement ; facture de 10 € + prorata des 19,90 € de service, au centime.
5. Vérifier **AwaitingPayment**, facture/quantité/montants/cycle affichés, aucune
   période ni budget IA. La facture est finalisée avec `AutoAdvance=false` :
   le MVP n’initie pas le paiement. La payer avec le moyen de paiement de test
   dans Stripe, sans la marquer payée manuellement hors Stripe. La preuve attendue
   reste un paiement Stripe réglé couvrant tout le montant TTC.
6. Laisser le webhook signé confirmer le paiement et terminer l’opération.
   **Actualiser les opérations** ne fait qu’un GET, sans appel métier de reprise.
   Vérifier **Completed**, confirmation UTC, ID période et ID événement.
7. Redélivrer le même événement Stripe et utiliser **Rejouer sans effet** : mêmes
   facture et période, aucun nouveau débit ni budget. En cas de réponse incertaine,
   **Reprendre l’opération** réutilise la même machine et les checkpoints durables.

Les paiements incomplets/incohérents n’accordent aucun budget. Un paiement tardif
ne crée pas de période expirée ; il nécessite une réconciliation. Une opération
inachevée bloque les ajouts suivants de l’entreprise. Aucun renouvellement mensuel,
wallet, remboursement, recharge ou changement de règle financière n’est ajouté.

## Routes

Préfixe Super Admin : `/api/companies/{companyId}/stripe`.

| Méthode | Suffixe | Effet |
|---|---|---|
| GET | vide | BillingAccount et machines actives, lecture SQL uniquement |
| POST | `/customer` | Customer existant ou création idempotente |
| POST | `/subscription` | Subscription existante ou création idempotente |
| GET | `/machine-additions` | Opérations, Stage, montants, dates, références |
| POST | `/machine-additions/{machineId}` | Lancement/reprise par le service existant |

Webhook conservé : `POST /api/stripe/webhooks/machine-additions`, signature Stripe
obligatoire. Les erreurs de commande retournent un champ `error` compatible avec
le client ; aucun détail sensible du fournisseur n’est envoyé au navigateur.

## Validation locale

`dotnet build backend/WebApp.sln` puis `dotnet test backend/WebApp.Api.Tests`.
Dans `frontend`, `npm run build` et `npm run test:run`.
Les tests utilisent SQLite en mémoire, gateways/HTTP simulés et événements signés
locaux. Ils n’appellent ni Stripe ni Azure et ne valident pas un déploiement réel.
