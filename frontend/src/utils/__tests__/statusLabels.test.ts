import { describe, expect, it } from 'vitest';
import {
  companyStatusLabel,
  diagnosticStatusLabel,
  invoiceStatusLabel,
  machineAdditionResultLabel,
  machineAdditionStageLabel,
  machineStatusLabel,
  subscriptionStatusLabel,
  userStatusLabel,
  walletTopUpStatusLabel,
} from '../statusLabels';

describe('status labels', () => {
  it('translates company, machine and user statuses according to their context', () => {
    expect(companyStatusLabel('active')).toBe('Active');
    expect(companyStatusLabel('inactive')).toBe('Inactive');
    expect(machineStatusLabel('active')).toBe('Active');
    expect(machineStatusLabel('inactive')).toBe('Inactive');
    expect(userStatusLabel('active')).toBe('Actif');
    expect(userStatusLabel('inactive')).toBe('Inactif');
  });

  it.each([
    ['active', 'Actif'],
    ['past_due', 'Paiement en retard'],
    ['unpaid', 'Impayé'],
    ['canceled', 'Résilié'],
    ['incomplete', 'Paiement initial en attente'],
    ['incomplete_expired', 'Paiement initial expiré'],
    ['trialing', 'Abonnement en cours d’activation'],
    ['paused', 'Suspendu'],
  ])('translates subscription status %s', (status, expected) => {
    expect(subscriptionStatusLabel(status)).toBe(expected);
  });

  it.each([
    ['draft', 'Brouillon'],
    ['open', 'En attente de paiement'],
    ['paid', 'Payée'],
    ['uncollectible', 'Irrécouvrable'],
    ['void', 'Annulée'],
  ])('translates invoice status %s', (status, expected) => {
    expect(invoiceStatusLabel(status)).toBe(expected);
  });

  it.each([
    ['Reserved', 'Réservation créée'],
    ['PaymentCreated', 'Paiement créé'],
    ['AwaitingPayment', 'En attente de paiement'],
    ['PaymentConfirmed', 'Paiement confirmé'],
    ['WalletCredited', 'Crédit ajouté au portefeuille'],
    ['Completed', 'Payé'],
  ])('translates wallet top-up status %s', (status, expected) => {
    expect(walletTopUpStatusLabel(status)).toBe(expected);
  });

  it.each([
    ['Reserved', 'Réservation créée'],
    ['StripeQuantityUpdated', 'Quantité Stripe mise à jour'],
    ['InvoiceFinalized', 'Facture finalisée'],
    ['AwaitingPayment', 'En attente de paiement'],
    ['PaymentConfirmed', 'Paiement confirmé'],
    ['MachineBillingPeriodCreated', 'Période de facturation créée'],
    ['Completed', 'Terminée'],
  ])('translates machine addition stage %s', (status, expected) => {
    expect(machineAdditionStageLabel(status)).toBe(expected);
  });

  it.each([
    ['AlreadyCompleted', 'Déjà terminée'],
    ['OperationNotReady', 'Opération pas encore prête'],
    ['ReconciliationRequired', 'Vérification manuelle requise'],
    ['AwaitingPayment', 'En attente de paiement'],
    ['Completed', 'Terminée'],
  ])('translates machine addition result %s', (status, expected) => {
    expect(machineAdditionResultLabel(status)).toBe(expected);
  });

  it('uses a safe fallback and keeps the raw code only in diagnostics', () => {
    expect(companyStatusLabel('unexpected')).toBe('Statut inconnu');
    expect(invoiceStatusLabel(undefined)).toBe('Statut inconnu');
    expect(diagnosticStatusLabel('unexpected', invoiceStatusLabel)).toBe('Statut inconnu (unexpected)');
    expect(diagnosticStatusLabel(null, invoiceStatusLabel)).toBe('—');
  });
});
