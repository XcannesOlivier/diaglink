import { PDFDocument } from 'pdf-lib';

export const MAX_PDF_COUNT = 10;
export const MAX_PDF_BYTES = 50 * 1024 * 1024;
export const MAX_COMBINED_PDF_BYTES = 200 * 1024 * 1024;

export type SelectedPdf = { id: number; file: File; pageCount: number };

export function formatFileSize(size: number) {
  if (size >= 1024 * 1024) return `${(size / (1024 * 1024)).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Mo`;
  return `${Math.max(1, Math.round(size / 1024)).toLocaleString('fr-FR')} Ko`;
}

export async function inspectPdfFiles(files: File[], current: SelectedPdf[], nextId: () => number) {
  const accepted: SelectedPdf[] = [];
  const errors: string[] = [];
  let combinedBytes = current.reduce((total, pdf) => total + pdf.file.size, 0);

  for (const file of files) {
    if (current.length + accepted.length >= MAX_PDF_COUNT) {
      errors.push(`Le nombre maximal de PDF est ${MAX_PDF_COUNT}.`);
      break;
    }
    if (file.type !== 'application/pdf' && !file.name.toLowerCase().endsWith('.pdf')) {
      errors.push(`${file.name} : seuls les fichiers PDF sont acceptés.`);
      continue;
    }
    if (file.size <= 0 || file.size > MAX_PDF_BYTES) {
      errors.push(`${file.name} : le fichier est vide ou dépasse 50 Mo.`);
      continue;
    }
    if (combinedBytes + file.size > MAX_COMBINED_PDF_BYTES) {
      errors.push('La taille totale des PDF ne peut pas dépasser 200 Mo.');
      continue;
    }
    try {
      const document = await PDFDocument.load(await file.arrayBuffer(), { updateMetadata: false });
      accepted.push({ id: nextId(), file, pageCount: document.getPageCount() });
      combinedBytes += file.size;
    } catch {
      errors.push(`${file.name} : ce PDF n’a pas pu être lu.`);
    }
  }
  return { accepted, errors };
}
