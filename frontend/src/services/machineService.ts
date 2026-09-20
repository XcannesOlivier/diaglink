import { getApiAuthHeaders } from '../utils/apiAuth';
import { parseApiResult, parseApiWriteResult } from '../utils/apiResponse';
import type { ApiResult, ApiWriteResult } from '../types/apiResult';
import type { MachineDto, MachineDocumentDto } from '../types/machine';

const getApiUrl = () => import.meta.env.VITE_API_URL || '/api';

export async function getMachineDocuments(
  getAccessToken: () => Promise<string | null>,
  machineId: string,
  signal?: AbortSignal,
): Promise<ApiResult<MachineDocumentDto[]>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/machines/${encodeURIComponent(machineId)}/documents`, { headers, signal });
    return await parseApiResult<MachineDocumentDto[]>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

export async function openMachineDocument(
  getAccessToken: () => Promise<string | null>,
  machineId: string,
  documentId: string,
): Promise<void> {
  // Reserve the tab during the click, before awaiting authentication/network requests.
  const tab = window.open('about:blank', '_blank');
  if (!tab) throw new Error("Autorisez l'ouverture d'un nouvel onglet.");
  tab.opener = null;
  try {
    const { headers } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/machines/${encodeURIComponent(machineId)}/documents/${encodeURIComponent(documentId)}`, { headers });
    if (!response.ok) throw new Error("Impossible d'ouvrir le document.");
    const blob = await response.blob();
    if (tab.closed) return;
    const url = URL.createObjectURL(new Blob([blob], { type: 'application/pdf' }));
    try { tab.location.replace(url); }
    catch (error) { URL.revokeObjectURL(url); throw error; }
    // Keep the URL alive for the PDF viewer; release it when its tab closes.
    const timer = window.setInterval(() => {
      if (tab.closed) {
        URL.revokeObjectURL(url);
        window.clearInterval(timer);
      }
    }, 1000);
  } catch (error) {
    tab.close();
    throw error;
  }
}

/** GET /api/machines — role-scoped list, entirely decided server-side (see MachineAccessService). */
export async function getMachines(
  getAccessToken: () => Promise<string | null>
): Promise<ApiResult<MachineDto[]>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/machines`, { headers });
    return await parseApiResult<MachineDto[]>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

/** GET /api/machines/{id} — backend returns 404 for both "doesn't exist" and "not accessible". */
export async function getMachineById(
  getAccessToken: () => Promise<string | null>,
  id: string
): Promise<ApiResult<MachineDto>> {
  try {
    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/machines/${encodeURIComponent(id)}`, { headers });
    return await parseApiResult<MachineDto>(response, mode);
  } catch {
    return { kind: 'error' };
  }
}

type AddMachineWithDocumentsResult = ApiWriteResult<unknown> | { kind: 'error'; message: string };

/** POST /api/files/upload — creates a machine and uploads its PDF documents. */
export async function addMachineWithDocuments(
  getAccessToken: () => Promise<string | null>,
  companyName: string,
  machineName: string,
  files: File[]
): Promise<AddMachineWithDocumentsResult> {
  try {
    const formData = new FormData();
    formData.append('companyName', companyName);
    formData.append('machineName', machineName);
    files.forEach(file => formData.append('files', file));

    const { headers, mode } = await getApiAuthHeaders(getAccessToken);
    const response = await fetch(`${getApiUrl()}/files/upload`, {
      method: 'POST',
      headers,
      body: formData,
    });

    if (!response.ok && response.status !== 400 && response.status !== 401 && response.status !== 403 && response.status !== 404 && response.status !== 409) {
      const message = await response.text();
      if (message.includes('BlobAlreadyExists') || message.includes('already exists')) {
        return { kind: 'error', message: 'Un fichier portant déjà ce nom existe pour cette machine. Aucun fichier n’a été remplacé.' };
      }
      return { kind: 'error', message: message || "Impossible d'ajouter la machine." };
    }

    return await parseApiWriteResult<unknown>(response, mode);
  } catch (error: unknown) {
    return { kind: 'error', message: error instanceof Error ? error.message : "Impossible d'ajouter la machine." };
  }
}
