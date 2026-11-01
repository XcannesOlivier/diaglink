import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { TechnicalSourceReference } from '../../../types/chat';
import { compactVerifiedSourceLines } from '../../../utils/technicalSources';
import { Markdown } from '../Markdown';

describe('Markdown technical sources', () => {
  let host: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    host = document.createElement('div');
    document.body.append(host);
    root = createRoot(host);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    host.remove();
  });

  const render = async (content: string, sources: TechnicalSourceReference[], onClick = vi.fn()) => {
    await act(async () => root.render(
      <Markdown content={content} sources={sources} onTechnicalSourceClick={onClick} />
    ));
    return onClick;
  };

  it('removes a simple Claude source line only when its citation is verified', () => {
    const content = 'Diagnostic confirmé.\n📄 Source : p. 16';
    const startIndex = content.indexOf('p. 16');
    const source = { id: 1, documentName: 'manual.pdf', pdfPage: 18, displayPage: '16', label: 'p. 16', startIndex, endIndex: startIndex + 5, displayOrder: 0 };

    const result = compactVerifiedSourceLines(content, [source]);

    expect(result.content).toBe('Diagnostic confirmé.\n');
    expect(result.sources).toEqual([]);
  });

  it('removes multiple verified citations from the same documentary line', () => {
    const content = 'Réponse.\n📄 Sources : p. 15 · p. 16';
    const first = content.indexOf('p. 15');
    const second = content.indexOf('p. 16');

    const result = compactVerifiedSourceLines(content, [
      { id: 1, documentName: 'manual.pdf', pdfPage: 17, displayPage: '15', label: 'p. 15', startIndex: first, endIndex: first + 5, displayOrder: 0 },
      { id: 2, documentName: 'manual.pdf', pdfPage: 18, displayPage: '16', label: 'p. 16', startIndex: second, endIndex: second + 5, displayOrder: 1 },
    ]);

    expect(result.content).toBe('Réponse.\n');
    expect(result.sources).toEqual([]);
  });

  it('keeps the unresolved part of a partially verified source line explicit', () => {
    const content = 'Réponse.\n📄 Sources : Page PDF : 214, 670';
    const verified = content.indexOf('214');

    const result = compactVerifiedSourceLines(content, [
      { id: 1, documentName: 'parts.pdf', pdfPage: 214, displayPage: '214', label: '214', startIndex: verified, endIndex: verified + 3, displayOrder: 0 },
    ]);

    expect(result.content).toBe('Réponse.\n📄 Références non vérifiées : Page PDF : 670');
    expect(result.content).toContain('670');
    expect(result.content).not.toContain('214');
  });

  it('adjusts later verified offsets after compacting a historical source line', () => {
    const content = '📄 Source : p. 16\nVoir aussi p. 22 dans le texte.';
    const first = content.indexOf('p. 16');
    const second = content.indexOf('p. 22');

    const result = compactVerifiedSourceLines(content, [
      { id: 1, documentName: 'manual.pdf', pdfPage: 18, displayPage: '16', label: 'p. 16', startIndex: first, endIndex: first + 5, displayOrder: 0 },
      { id: 2, documentName: 'manual.pdf', pdfPage: 24, displayPage: '22', label: 'p. 22', startIndex: second, endIndex: second + 5, displayOrder: 1 },
    ]);

    expect(result.content).toBe('Voir aussi p. 22 dans le texte.');
    expect(result.sources).toHaveLength(1);
    expect(result.content.slice(result.sources[0].startIndex, result.sources[0].endIndex)).toBe('p. 22');
  });

  it('renders exactly one structured label as an accessible button', async () => {
    const content = 'Source : p. 72';
    const source = { id: 123, pdfPage: 74, displayPage: '72', label: 'p. 72', startIndex: 9, endIndex: 14, displayOrder: 0 };
    const onClick = await render(content, [source]);

    const button = host.querySelector<HTMLButtonElement>('button[aria-label="Ouvrir la source page 72"]');
    expect(button?.textContent).toBe('p. 72');
    expect(host.textContent?.trimEnd()).toBe(content);
    await act(async () => button?.click());
    expect(onClick).toHaveBeenCalledWith(source);
  });

  it('renders multiple sources without duplicating Markdown text', async () => {
    const content = '**Source** : p. 72 (...) et p. 73 (...)';
    const first = content.indexOf('p. 72');
    const second = content.indexOf('p. 73');
    await render(content, [
      { id: 123, pdfPage: 74, displayPage: '72', label: 'p. 72', startIndex: first, endIndex: first + 5, displayOrder: 0 },
      { id: 124, pdfPage: 75, displayPage: '73', label: 'p. 73', startIndex: second, endIndex: second + 5, displayOrder: 1 },
    ]);

    expect(host.querySelectorAll('button')).toHaveLength(2);
    expect(host.querySelector('strong')?.textContent).toBe('Source');
    expect(host.textContent?.trimEnd()).toBe('Source : p. 72 (...) et p. 73 (...)');
  });

  it('keeps UTF-16 offsets aligned after accents, apostrophes, and an emoji', async () => {
    const content = 'Après l’arrêt 🛠️, voir p. 72.';
    const startIndex = content.indexOf('p. 72');
    await render(content, [
      { id: 123, pdfPage: 74, displayPage: '72', label: 'p. 72', startIndex, endIndex: startIndex + 5, displayOrder: 0 },
    ]);

    expect(host.querySelector('button')?.textContent).toBe('p. 72');
    expect(host.textContent).toContain('Après l’arrêt 🛠️, voir p. 72.');
  });

  it('does not confuse an existing Markdown fragment with a structured source', async () => {
    const content = '[Autre lien](#diaglink-source-123), puis p. 72.';
    const startIndex = content.indexOf('p. 72');
    await render(content, [
      { id: 123, pdfPage: 74, displayPage: '72', label: 'p. 72', startIndex, endIndex: startIndex + 5, displayOrder: 0 },
    ]);

    expect(host.querySelector('a')?.textContent).toBe('Autre lien');
    expect(host.querySelector('a')?.getAttribute('href')).toBe('#diaglink-source-123');
    expect(host.querySelector('button')?.textContent).toBe('p. 72');
  });

  it('renders normal text for invalid or overlapping ranges', async () => {
    const content = 'Source : p. 72';
    await render(content, [
      { id: 123, pdfPage: 74, displayPage: '72', label: 'p. 72', startIndex: 9, endIndex: 14, displayOrder: 0 },
      { id: 124, pdfPage: 74, displayPage: '72', label: '. 72', startIndex: 10, endIndex: 14, displayOrder: 1 },
      { id: 125, pdfPage: 74, displayPage: '72', label: 'p. 72', startIndex: 99, endIndex: 104, displayOrder: 2 },
    ]);

    expect(host.querySelector('button')).toBeNull();
    expect(host.textContent?.trimEnd()).toBe(content);
  });
});
