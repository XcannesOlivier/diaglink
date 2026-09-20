/**
 * Error types and interfaces for structured error handling
 */

export type ErrorCode = 
  | 'NETWORK'      // Connection/fetch failures, 5xx errors
  | 'AUTH'         // 401/403 authentication errors
  | 'STREAM'       // SSE streaming errors
  | 'SERVER'       // 400/500 server response errors
  | 'API'          // REST API errors (conversation management, etc.)
  | 'AiCreditExhausted'
  | 'UNKNOWN';     // Unclassified errors

export interface AppError {
  code: ErrorCode;
  message: string;
  recoverable: boolean;
  action?: {
    label: string;
    handler: () => void;
  };
  originalError?: Error;
}

/**
 * Error messages for different error codes
 */
export const ERROR_MESSAGES: Record<ErrorCode, string> = {
  AiCreditExhausted: 'Crédit IA épuisé. Rechargez le portefeuille de votre entreprise pour continuer.',
  NETWORK: 'Impossible de se connecter au serveur. Vérifiez votre connexion internet et réessayez.',
  AUTH: 'Votre session a expiré. Veuillez vous reconnecter pour continuer.',
  STREAM: 'La réponse a été interrompue. Cliquez sur Réessayer pour poursuivre la conversation.',
  SERVER: 'Le serveur a rencontré une erreur. Elle a été enregistrée et notre équipe va l’examiner.',
  API: 'La requête a échoué. Veuillez réessayer.',
  UNKNOWN: 'Une erreur inattendue est survenue. Veuillez réessayer ou contacter le support si le problème persiste.',
};

/**
 * Detailed user-friendly messages with recovery hints
 */
export const DETAILED_ERROR_MESSAGES: Record<ErrorCode, { title: string; description: string; hint: string }> = {
  AiCreditExhausted: { title: 'Crédit IA épuisé', description: 'Le crédit IA disponible est épuisé.', hint: 'Rechargez le portefeuille de votre entreprise, puis renvoyez votre message. L’historique reste accessible.' },
  NETWORK: {
    title: 'Connexion perdue',
    description: 'Impossible de joindre le serveur.',
    hint: 'Vérifiez votre connexion internet et réessayez.'
  },
  AUTH: {
    title: 'Session expirée',
    description: 'Votre session d’authentification a expiré.',
    hint: 'Cliquez sur « Se reconnecter » ci-dessous pour continuer.'
  },
  STREAM: {
    title: 'Réponse interrompue',
    description: 'La réponse de l’IA a été interrompue de manière inattendue.',
    hint: 'Cliquez sur « Réessayer » pour renvoyer votre message.'
  },
  SERVER: {
    title: 'Erreur serveur',
    description: 'Le serveur a rencontré une erreur inattendue.',
    hint: 'Veuillez réessayer dans quelques instants.'
  },
  API: {
    title: 'Requête échouée',
    description: 'La requête API n’a pas pu être complétée.',
    hint: 'Veuillez réessayer.'
  },
  UNKNOWN: {
    title: 'Erreur inattendue',
    description: 'Une erreur est survenue.',
    hint: 'Essayez de recharger la page ou contactez le support si le problème persiste.'
  }
};

/**
 * Determine if an error is recoverable
 */
export function isRecoverableError(code: ErrorCode): boolean {
  // AUTH requires re-login, UNKNOWN may indicate critical failure
  // NETWORK, STREAM, and SERVER errors are typically recoverable with retry
  return code !== 'AiCreditExhausted' && code !== 'AUTH' && code !== 'UNKNOWN';
}

/**
 * Type guard to check if an unknown value is an AppError
 */
export function isAppError(error: unknown): error is AppError {
  return (
    error !== null &&
    typeof error === 'object' &&
    'code' in error &&
    'message' in error &&
    'recoverable' in error
  );
}

