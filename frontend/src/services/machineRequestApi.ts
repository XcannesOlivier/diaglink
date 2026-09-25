import type { StartFormValues } from '../pages/start/startFormValidation';

export type MachineRequestResponse = {
  requestId: string;
  status: string;
  createdAt: string;
  documentCount: number;
  totalPages: number;
  additionalPages: number;
  preparationTotal: number;
  authorizationInsufficient: boolean;
};

export class MachineRequestSubmissionError extends Error {
  readonly isNetworkError: boolean;

  constructor(message: string, isNetworkError = false) {
    super(message);
    this.name = 'MachineRequestSubmissionError';
    this.isNetworkError = isNetworkError;
  }
}

export function buildMachineRequestFormData(values: StartFormValues, files: File[], paymentRequestId: string) {
  const formData = new FormData();
  formData.append('paymentRequestId', paymentRequestId);
  formData.append('firstName', values.firstName);
  formData.append('lastName', values.lastName);
  formData.append('company', values.company);
  formData.append('email', values.email);
  formData.append('phone', values.phone);
  formData.append('machineName', values.machineName);
  formData.append('manufacturer', values.manufacturer);
  formData.append('model', values.model);
  if (values.serialNumber) formData.append('serialNumber', values.serialNumber);
  if (values.description) formData.append('description', values.description);
  files.forEach(file => formData.append('documents', file, file.name));
  return formData;
}

function getSafeApiError(payload: unknown) {
  if (!payload || typeof payload !== 'object') return null;
  const problem = payload as { errors?: Record<string, unknown>; title?: unknown };
  if (problem.errors && typeof problem.errors === 'object') {
    for (const messages of Object.values(problem.errors)) {
      if (Array.isArray(messages) && typeof messages[0] === 'string') return messages[0];
    }
  }
  return typeof problem.title === 'string' ? problem.title : null;
}

export async function submitMachineRequest(values: StartFormValues, files: File[], paymentRequestId: string): Promise<MachineRequestResponse> {
  let response: Response;
  try {
    response = await fetch(`${import.meta.env.VITE_API_URL || '/api'}/public/machine-requests`, {
      method: 'POST',
      body: buildMachineRequestFormData(values, files, paymentRequestId),
    });
  } catch {
    throw new MachineRequestSubmissionError('Impossible de contacter le service pour le moment. Veuillez réessayer.', true);
  }

  if (!response.ok) {
    let message: string | null = null;
    try { message = getSafeApiError(await response.json()); } catch { /* The API may return an empty error response. */ }
    throw new MachineRequestSubmissionError(message || 'La demande n’a pas pu être envoyée. Vérifiez les informations et réessayez.');
  }

  return await response.json() as MachineRequestResponse;
}
