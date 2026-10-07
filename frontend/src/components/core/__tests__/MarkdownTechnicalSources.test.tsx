import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { TechnicalSourceReference } from '../../../types/chat';
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