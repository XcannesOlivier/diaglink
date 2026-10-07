import type { IFileAttachment } from '../types/chat';

export interface FileConversionResult {
  name: string;
  dataUri: string;
  mimeType: string;
  sizeBytes: number;
}

export interface FileValidationResult {
  valid: boolean;
  error?: string;
}

const MAX_IMAGE_SIZE = 5 * 1024 * 1024;
const MAX_IMAGE_COUNT = 5;
const ALLOWED_IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/jpg'];

export const CLAUDE_DIRECT_FILE_INPUT_ACCEPT = 'image/png,image/jpeg,image/jpg';

const EXTENSION_TO_MIME: Record<string, string> = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
};

export function getEffectiveMimeType(file: File): string {
  if (file.type && file.type !== 'application/octet-stream') {
    return file.type.toLowerCase();
  }

  const extension = file.name.toLowerCase().match(/\.[^.]+$/)?.[0] || '';
  return EXTENSION_TO_MIME[extension] || file.type || '';
}

export function validateImageFile(file: File): FileValidationResult {
  const mimeType = getEffectiveMimeType(file);

  if (!mimeType.startsWith('image/')) {
    return { valid: false, error: `"${file.name}" n'est pas un fichier image` };
  }

  if (!ALLOWED_IMAGE_TYPES.includes(mimeType)) {
    return { valid: false, error: `Format de "${file.name}" non pris en charge. Utilisez PNG ou JPEG` };
  }

  if (file.size > MAX_IMAGE_SIZE) {
    const sizeMB = (file.size / (1024 * 1024)).toFixed(1);
    return { valid: false, error: `"${file.name}" fait ${sizeMB} Mo. La taille maximale est de 5 Mo` };
  }

  return { valid: true };
}

export function validateFile(file: File): FileValidationResult {
  if (!getEffectiveMimeType(file).startsWith('image/')) {
    return {
      valid: false,
      error: 'Le chat accepte uniquement les images PNG et JPEG. Les PDF techniques doivent être ajoutés aux documents de la machine.',
    };
  }

  return validateImageFile(file);
}

export function validateFileCount(
  files: File[],
  currentFileCount: number = 0,
): FileValidationResult {
  const totalCount = currentFileCount + files.length;

  if (totalCount > MAX_IMAGE_COUNT) {
    return {
      valid: false,
      error: `Maximum ${MAX_IMAGE_COUNT} images autorisées. Vous en avez déjà ${currentFileCount} jointes et essayez d'en ajouter ${files.length} de plus`,
    };
  }

  return { valid: true };
}

export function getMaximumAttachmentCount(): number {
  return MAX_IMAGE_COUNT;
}

export function getFileInputAccept(): string {
  return CLAUDE_DIRECT_FILE_INPUT_ACCEPT;
}

async function convertFileToDataUri(file: File, effectiveMimeType: string): Promise<string> {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = reader.result as string;
      const commaIndex = result.indexOf(',');
      if (commaIndex === -1) {
        reject(new Error('Invalid data URI format'));
        return;
      }
      resolve(`data:${effectiveMimeType};base64,${result.substring(commaIndex + 1)}`);
    };
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

export async function convertFilesToDataUris(files: File[]): Promise<FileConversionResult[]> {
  const results: FileConversionResult[] = [];

  for (const file of files) {
    const validation = validateFile(file);
    if (!validation.valid) {
      throw new Error(validation.error);
    }

    const effectiveMimeType = getEffectiveMimeType(file);
    results.push({
      name: file.name,
      dataUri: await convertFileToDataUri(file, effectiveMimeType),
      mimeType: effectiveMimeType,
      sizeBytes: file.size,
    });
  }

  return results;
}

export function createAttachmentMetadata(results: FileConversionResult[]): IFileAttachment[] {
  return results.map((result) => ({
    fileName: result.name,
    fileSizeBytes: result.sizeBytes,
    dataUri: result.dataUri,
  }));
}
