import { beforeEach, describe, expect, it, vi } from 'vitest';

const { loadPdf } = vi.hoisted(() => ({ loadPdf: vi.fn() }));

vi.mock('pdf-lib', () => ({ PDFDocument: { load: loadPdf } }));

import {
  MAX_COMBINED_PDF_BYTES,
  MAX_PDF_BYTES,
  MAX_PDF_COUNT,
  inspectPdfFiles,
} from './pdfSelection';

function pdf(name: string, size: number, type = 'application/pdf') {
  const file = new File([new Uint8Array([0x25, 0x50, 0x44, 0x46])], name, { type });
  Object.defineProperty(file, 'size', { configurable: true, value: size });
  Object.defineProperty(file, 'arrayBuffer', {
    configurable: true,
    value: vi.fn().mockResolvedValue(new Uint8Array([0x25, 0x50, 0x44, 0x46]).buffer),
  });
  return file;
}

describe('inspectPdfFiles PDF limits', () => {
  beforeEach(() => {
    loadPdf.mockReset();
    loadPdf.mockResolvedValue({ getPageCount: () => 37 });
  });

  it('exposes the 200 Mio per-file and combined limits with ten files maximum', () => {
    expect(MAX_PDF_BYTES).toBe(200 * 1024 * 1024);
    expect(MAX_COMBINED_PDF_BYTES).toBe(200 * 1024 * 1024);
    expect(MAX_PDF_COUNT).toBe(10);
  });

  it('accepts one 200 Mio PDF without changing its page count', async () => {
    const result = await inspectPdfFiles([pdf('manual.pdf', MAX_PDF_BYTES)], [], () => 1);

    expect(result.errors).toEqual([]);
    expect(result.accepted).toHaveLength(1);
    expect(result.accepted[0].pageCount).toBe(37);
  });

  it('rejects one PDF above 200 Mio', async () => {
    const result = await inspectPdfFiles([pdf('manual.pdf', MAX_PDF_BYTES + 1)], [], () => 1);

    expect(result.accepted).toEqual([]);
    expect(result.errors).toEqual(['manual.pdf : le fichier est vide ou dépasse 200 Mio.']);
    expect(loadPdf).not.toHaveBeenCalled();
  });

  it('accepts an exact 200 Mio combined total and rejects a larger total', async () => {
    const half = MAX_COMBINED_PDF_BYTES / 2;
    const exact = await inspectPdfFiles(
      [pdf('one.pdf', half), pdf('two.pdf', half)], [], (() => { let id = 0; return () => ++id; })(),
    );
    const over = await inspectPdfFiles(
      [pdf('one.pdf', half), pdf('two.pdf', half + 1)], [], (() => { let id = 0; return () => ++id; })(),
    );

    expect(exact.accepted).toHaveLength(2);
    expect(exact.errors).toEqual([]);
    expect(over.accepted).toHaveLength(1);
    expect(over.errors).toEqual(['La taille totale des PDF ne peut pas dépasser 200 Mio.']);
  });

  it('accepts ten files and rejects an eleventh', async () => {
    const ten = Array.from({ length: MAX_PDF_COUNT }, (_, index) => pdf(`${index}.pdf`, 1));
    const accepted = await inspectPdfFiles(ten, [], (() => { let id = 0; return () => ++id; })());
    const rejected = await inspectPdfFiles([...ten, pdf('extra.pdf', 1)], [], (() => { let id = 0; return () => ++id; })());

    expect(accepted.accepted).toHaveLength(10);
    expect(accepted.errors).toEqual([]);
    expect(rejected.accepted).toHaveLength(10);
    expect(rejected.errors).toEqual(['Le nombre maximal de PDF est 10.']);
  });

  it('rejects empty, non-PDF and unreadable PDF files', async () => {
    loadPdf.mockRejectedValueOnce(new Error('invalid PDF'));

    const result = await inspectPdfFiles([
      pdf('empty.pdf', 0),
      pdf('notes.txt', 10, 'text/plain'),
      pdf('broken.pdf', 10),
    ], [], () => 1);

    expect(result.accepted).toEqual([]);
    expect(result.errors).toHaveLength(3);
    expect(result.errors[0]).toContain('vide ou dépasse 200 Mio');
    expect(result.errors[1]).toContain('seuls les fichiers PDF sont acceptés');
    expect(result.errors[2]).toContain('ce PDF n’a pas pu être lu');
  });
});
