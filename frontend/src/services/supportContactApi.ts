import type { ApiWriteResult } from '../types/apiResult';
import { getApiAuthHeaders } from '../utils/apiAuth';
import { parseApiWriteResult } from '../utils/apiResponse';

const apiUrl = import.meta.env.VITE_API_URL || '/api';

export interface SupportContactRequest {
  message: string;
  machineId?: string;
}

export interface SupportContactResponse {
  success: boolean;
}

export async function submitSupportContact(
  getAccessToken: () => Promise<string | null>,
  request: SupportContactRequest,
): Promise<ApiWriteResult<SupportContactResponse>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${apiUrl}/contact`, {
      method: 'POST',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    return await parseApiWriteResult<SupportContactResponse>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}