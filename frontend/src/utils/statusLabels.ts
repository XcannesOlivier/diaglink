type StatusLabels = Readonly<Record<string, string>>;

const labelStatus = (value: string | null | undefined, labels: StatusLabels): string =>
  value ? labels[value] ?? 'Statut inconnu' : 'Statut inconnu';

const COMPANY_AND_MACHINE_STATUS_LABELS: StatusLabels = {
  active: 'Active',
  inactive: 'Inactive',
};

const USER_STATUS_LABELS: StatusLabels = {
  active: 'Actif',
  inactive: 'Inactif',
};

const SUBSCRIPTION_STATUS_LABELS: StatusLabels = {
  active: 'Actif',
  past_due: 'Paiement en retard',
  unpaid: 'Impayé',
  canceled: 'Résilié',
  incomplete: 'Paiement initial en attente',
  incomplete_expired: 'Paiement initial expiré',
  trialing: 'Abonnement en cours d’activation',
  paused: 'Suspendu',
};

const INVOICE_STATUS_LABELS: StatusLabels = {
  draft: 'Brouillon',
  open: 'En attente de paiement',
  paid: 'Payée',
  uncollectible: 'Irrécouvrable',
  void: 'Annulée',
};

const WALLET_TOP_UP_STATUS_LABELS: StatusLabels = {
  Reserved: 'Réservation créée',
  PaymentCreated: 'Paiement créé',
  AwaitingPayment: 'En attente de paiement',
  PaymentConfirmed: 'Paiement confirmé',
  WalletCredited: 'Crédit ajouté au portefeuille',
  Completed: 'Payé',
};

const MACHINE_ADDITION_STAGE_LABELS: StatusLabels = {
  Reserved: 'Réservation créée',
  StripeQuantityUpdated: 'Quantité Stripe mise à jour',
  InvoiceFinalized: 'Facture finalisée',
  AwaitingPayment: 'En attente de paiement',
  PaymentConfirmed: 'Paiement confirmé',
  MachineBillingPeriodCreated: 'Période de facturation créée',
  Completed: 'Terminée',
};

const MACHINE_ADDITION_RESULT_LABELS: StatusLabels = {
  AlreadyCompleted: 'Déjà terminée',
  OperationNotReady: 'Opération pas encore prête',
  ReconciliationRequired: 'Vérification manuelle requise',
  AwaitingPayment: 'En attente de paiement',
  Completed: 'Terminée',
};

export const companyStatusLabel = (value: string | null | undefined): string =>
  labelStatus(value, COMPANY_AND_MACHINE_STATUS_LABELS);

export const machineStatusLabel = (value: string | null | undefined): string =>
  labelStatus(value, COMPANY_AND_MACHINE_STATUS_LABELS);

export const userStatusLabel = (value: string | null | undefined): string =>
  labelStatus(value, USER_STATUS_LABELS);

export const subscriptionStatusLabel = (value: string | null | undefined): string =>
  labelStatus(value, SUBSCRIPTION_STATUS_LABELS);

export const invoiceStatusLabel = (value: string | null | undefined): string =>
  labelStatus(value, INVOICE_STATUS_LABELS);

export const walletTopUpStatusLabel = (value: string | null | undefined): string =>
  labelStatus(value, WALLET_TOP_UP_STATUS_LABELS);

export const machineAdditionStageLabel = (value: string | null | undefined): string =>
  labelStatus(value, MACHINE_ADDITION_STAGE_LABELS);

export const machineAdditionResultLabel = (value: string | null | undefined): string =>
  labelStatus(value, MACHINE_ADDITION_RESULT_LABELS);

export const diagnosticStatusLabel = (
  value: string | null | undefined,
  formatter: (status: string | null | undefined) => string,
): string => value ? `${formatter(value)} (${value})` : '—';
