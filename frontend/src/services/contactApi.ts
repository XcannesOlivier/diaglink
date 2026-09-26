export type ContactFormValues = {
  name: string;
  company: string;
  email: string;
  phone: string;
  message: string;
};

export class ContactSubmissionError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ContactSubmissionError';
  }
}

function getSafeApiError(payload: unknown) {
  if (!payload || typeof payload !== 'object') return null;
  const problem = payload as { error?: unknown; errors?: Record<string, unknown>; title?: unknown };
  if (typeof problem.error === 'string') return problem.error;
  if (problem.errors && typeof problem.errors === 'object') {
    for (const messages of Object.values(problem.errors)) {
      if (Array.isArray(messages) && typeof messages[0] === 'string') return messages[0];
    }
  }
  return typeof problem.title === 'string' ? problem.title : null;
}

export async function submitContact(values: ContactFormValues): Promise<void> {
  let response: Response;
  try {
    response = await fetch(`${import.meta.env.VITE_API_URL || '/api'}/public/contact`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(values),
    });
  } catch {
    throw new ContactSubmissionError('Impossible de contacter le service. Vérifiez votre connexion et réessayez.');
  }

  let payload: unknown = null;
  try { payload = await response.json(); } catch { /* The API may return an empty error response. */ }

  if (!response.ok) {
    throw new ContactSubmissionError(getSafeApiError(payload)
      ?? 'Le message n’a pas pu être envoyé. Veuillez réessayer plus tard.');
  }

  if (!payload || typeof payload !== 'object' || (payload as { success?: unknown }).success !== true) {
    throw new ContactSubmissionError('Le service n’a pas confirmé l’envoi du message. Veuillez réessayer.');
  }
}