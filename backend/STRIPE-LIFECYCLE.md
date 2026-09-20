# Impayés, résiliation et statut machine — Stripe test

La destination existante `/api/stripe/webhooks/machine-additions` conserve sa
signature obligatoire (`STRIPE_WEBHOOK_SECRET`). Événements à configurer :

- `invoice.payment_succeeded` : création/renouvellement des budgets après paiement vérifié ;
- `invoice.payment_failed`, `invoice.finalization_failed` : synchronisation sans attribution ;
- `customer.subscription.updated`, `customer.subscription.deleted` : statut courant et résiliation ;
- `invoice.upcoming` : contrôle de la quantité avant génération de la facture.

Le traitement relit Stripe, sous verrou entreprise, au lieu de recopier le statut
d'un ancien événement. `StripeLifecycleEvents` déduplique les événements et garde
les commandes de statut machine avec leur identifiant de reprise. Les échecs
temporaires demandent une nouvelle livraison ; aucune période n'est créée par les
événements d'échec, de statut ou d'annonce de facture.

La résiliation est demandée dans Stripe test (normalement `cancel_at_period_end`).
Les périodes payées ne sont ni supprimées, ni raccourcies. Pour une entreprise liée
à Stripe, l'accès IA reste valable pendant la période payée puis expire, même si le
statut machine reste `active`. Les entreprises sans abonnement gardent leur règle
d'accès historique. Les autorisations de rôle/entreprise/affectation restent exigées.

## Machines

Super Admin : `POST /api/companies/{companyId}/stripe/machines/{machineId}/status`,
corps `{ "active": false, "requestId": "<UUID stable>" }`.
Le statut est modifié avec la quantité Stripe en valeur absolue, `proration_behavior=none` :
aucune modification de facture déjà payée, aucun remboursement et aucun changement
d'ancrage. La quantité zéro est autorisée. La prochaine facture utilise les machines
actives ; `invoice.upcoming` effectue également une vérification avant émission.

Une réactivation pendant une période déjà payée réutilise cette période sans autre
facture ni budget. Sans période courante, elle reprend `StripeMachineAdditionService` :
10 EUR IA + prorata service, période uniquement après paiement. Les anciennes périodes
et opérations sont conservées ; l'unicité des ajouts devient machine + cycle.
Une commande incertaine conserve son UUID dans le navigateur. Les ajouts terminés
ne sont plus relancés depuis la liste historique, pour ne pas démarrer un nouveau cycle.

## Schéma / UI

Migration `20260914193125_AddStripeLifecycle`, créée localement, non appliquée :
table `StripeLifecycleEvents`, quatre champs de suivi dans `BillingAccounts`,
index d'ajout par machine/cycle et autorisation d'une quantité initiale zéro.
Super Admin affiche statut, résiliation à échéance, dernière facture / montant dû,
machines facturables ou non et échéance des périodes payées. Boutons désactiver/réactiver.

## Limites opérationnelles

- Configurer les événements Stripe et appliquer séparément la migration avant déploiement.
- Utiliser les commandes backend de statut ; un changement SQL direct ne synchronise pas Stripe.
- Une facture déjà émise avec une quantité différente n'est pas réécrite : le paiement
  incohérent reste en réconciliation, sans attribution supplémentaire automatique.
- Une opération d'ajout ou de changement de statut inachevée bloque les changements
  concurrents ; reprendre la même commande/opération avant de poursuivre.
- Les factures historiques hors cycle courant et les périodes incompatibles restent
  en réconciliation. Aucun remboursement ni avoir n'est implémenté.
- Valider désactivation proche d'échéance, échec puis règlement, et résiliation avec
  Stripe test clocks. Aucun accès Azure ou Stripe réel pendant l'implémentation.

Références : [proratas Stripe](https://docs.stripe.com/billing/subscriptions/prorations),
[résiliation Stripe](https://docs.stripe.com/billing/subscriptions/cancel).
