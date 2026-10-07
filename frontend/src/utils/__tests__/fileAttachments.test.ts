import { describe, expect, it } from 'vitest';
import {
  convertFilesToDataUris,
  getEffectiveMimeType,
  getFileInputAccept,
  getMaximumAttachmentCount,
  validateFile,
  validateFileCount,
  validateImageFile,
} from '../fileAttachments';

function file(name: string, type: string, size: number): File {
  const value = new File([''], name, { type });
  Object.defineProperty(value, 'size', { value: size, writable: false });
  return value;
}

describe('Claude Direct chat attachments', () => {
  it.each([
    ['photo.png', 'image/png'],
    ['photo.jpeg', 'image/jpeg'],
    ['photo.jpg', 'image/jpg'],
  ])('accepts %s', (name, type) => {
    expect(validateFile(file(name, type, 1024)).valid).toBe(true);
  });

  it.each([
    ['manual.pdf', 'application/pdf'],
    ['notes.txt', 'text/plain'],
    ['animation.gif', 'image/gif'],
    ['photo.webp', 'image/webp'],
  ])('rejects %s', (name, type) => {
    const result = validateFile(file(name, type, 1024));
    expect(result.valid).toBe(false);
  });

  it('directs chat PDFs to machine documents', () => {
    const result = validateFile(file('manual.pdf', 'application/pdf', 1024));
    expect(result.error).toContain('documents de la machine');
  });

  it('rejects images larger than 5 MB', () => {
    expect(validateImageFile(file('large.png', 'image/png', 5 * 1024 * 1024 + 1)).error)
      .toContain('5 Mo');
  });

  it('accepts exactly 5 images and rejects the sixth', () => {
    expect(validateFileCount([file('fifth.png', 'image/png', 1)], 4).valid).toBe(true);
    const result = validateFileCount([file('sixth.png', 'image/png', 1)], 5);
    expect(result.valid).toBe(false);
    expect(result.error).toContain('Maximum 5 images');
  });

  it('publishes only the PNG/JPEG picker contract', () => {
    expect(getMaximumAttachmentCount()).toBe(5);
    expect(getFileInputAccept()).toBe('image/png,image/jpeg,image/jpg');
  });

  it('detects supported image MIME types from extensions', () => {
    expect(getEffectiveMimeType(file('photo.png', '', 1))).toBe('image/png');
    expect(getEffectiveMimeType(file('photo.jpeg', 'application/octet-stream', 1))).toBe('image/jpeg');
  });

  it('converts a supported image to a data URI', async () => {
    const results = await convertFilesToDataUris([file('photo.png', 'image/png', 100)]);
    expect(results).toHaveLength(1);
    expect(results[0].mimeType).toBe('image/png');
    expect(results[0].dataUri).toMatch(/^data:image\/png;base64,/);
  });

  it('refuses document conversion', async () => {
    await expect(convertFilesToDataUris([file('manual.pdf', 'application/pdf', 100)]))
      .rejects.toThrow('documents de la machine');
  });
});
