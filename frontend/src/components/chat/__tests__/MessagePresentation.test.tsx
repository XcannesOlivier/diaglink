import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { UserMessage } from '../UserMessage';
import { AssistantMessage } from '../AssistantMessage';

vi.mock('@fluentui-copilot/react-copilot-chat', () => ({
  UserMessage: ({ children, timestamp }: { children: ReactNode; timestamp?: string }) => (
    <article data-testid="user-message">
      {timestamp && <time>{timestamp}</time>}
      {children}
    </article>
  ),
  CopilotMessage: ({ children, footnote }: { children: ReactNode; footnote?: ReactNode }) => (
    <article data-testid="assistant-message">
      {children}
      {footnote}
    </article>
  ),
}));

vi.mock('../../core/Markdown', () => ({
  Markdown: ({ content, sources }: { content: string; sources?: Array<{ label: string }> }) => (
    <p data-source-count={sources?.length ?? 0} data-source-label={sources?.[0]?.label}>{content}</p>
  ),
}));
vi.mock('../../../hooks/useFormatTimestamp', () => ({ useFormatTimestamp: () => () => 'à l’instant' }));

describe('chat message presentation', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: vi.fn(() => 'blob:technical-visual'),
      revokeObjectURL: vi.fn(),
    });
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
  });

  it('keeps only the assistant timestamp and omits feedback actions', async () => {
    await act(async () => root.render(
      <>
        <UserMessage message={{ id: 'user-1', role: 'user', content: 'Question', more: { time: '2026-10-01T10:00:00Z' } }} />
        <AssistantMessage
          message={{ id: 'assistant-1', role: 'assistant', content: 'Réponse', more: { time: '2026-10-01T10:00:01Z' } }}
          onRegenerate={vi.fn()}
          onFeedback={vi.fn()}
        />
      </>,
    ));

    const userMessage = container.querySelector<HTMLElement>('[data-testid="user-message"]');
    const assistantMessage = container.querySelector<HTMLElement>('[data-testid="assistant-message"]');

    expect(userMessage?.querySelector('time')).toBeNull();
    expect(userMessage?.textContent).not.toContain('à l’instant');
    expect(assistantMessage?.textContent).toContain('à l’instant');
    expect(assistantMessage?.querySelector('button[aria-label="Bonne réponse"]')).toBeNull();
    expect(assistantMessage?.querySelector('button[aria-label="Mauvaise réponse"]')).toBeNull();
    expect(assistantMessage?.querySelector('button[aria-label="Copier le message"]')).not.toBeNull();
    expect(assistantMessage?.querySelector('button[aria-label="Régénérer la réponse"]')).not.toBeNull();
  });

  it('keeps file-search citations as non-downloadable documentary references', async () => {
    await act(async () => root.render(
      <AssistantMessage
        message={{
          id: 'citation',
          role: 'assistant',
          content: 'Consultez la documentation.',
          annotations: [{ type: 'file_citation', label: 'manuel.pdf', fileId: 'file-search-id' }],
        }}
      />,
    ));

    expect(container.textContent).toContain('manuel.pdf');
    expect(container.querySelector('[aria-label="Télécharger manuel.pdf"]')).toBeNull();
  });

  it('renders deduplicated Web citations as safe external links', async () => {
    await act(async () => root.render(
      <AssistantMessage
        message={{
          id: 'web-citations',
          role: 'assistant',
          content: 'Answer with a Web source.',
          annotations: [
            {
              type: 'uri_citation',
              label: 'Manufacturer documentation',
              url: 'https://example.test/technical-article',
              quote: 'Quoted technical passage.',
            },
            {
              type: 'uri_citation',
              label: 'Duplicate',
              url: 'https://example.test/technical-article',
            },
          ],
        }}
      />,
    ));

    const section = container.querySelector('section[aria-labelledby="web-sources-web-citations"]');
    const links = section?.querySelectorAll<HTMLAnchorElement>('a');
    expect(section?.textContent).toContain('Sources Web');
    expect(section?.textContent).toContain('Manufacturer documentation');
    expect(section?.textContent).toContain('Quoted technical passage.');
    expect(section?.textContent).not.toContain('Duplicate');
    expect(links).toHaveLength(1);
    expect(links?.[0].getAttribute('href')).toBe('https://example.test/technical-article');
    expect(links?.[0].getAttribute('target')).toBe('_blank');
    expect(links?.[0].getAttribute('rel')).toBe('noopener noreferrer');
  });

  it('keeps private PDF sources separate from Web citations', async () => {
    await act(async () => root.render(
      <AssistantMessage
        message={{
          id: 'mixed-sources',
          role: 'assistant',
          content: 'Source: p. 70.',
          sources: [{
            id: 123,
            pdfPage: 72,
            displayPage: '70',
            label: 'p. 70',
            startIndex: 8,
            endIndex: 13,
            displayOrder: 0,
          }],
          annotations: [{
            type: 'uri_citation',
            label: 'Public source',
            url: 'https://example.test/public-source',
          }],
        }}
      />,
    ));

    const markdown = container.querySelector('[data-source-count]');
    expect(markdown?.getAttribute('data-source-count')).toBe('1');
    expect(markdown?.getAttribute('data-source-label')).toBe('p. 70');
    expect(container.querySelectorAll('section[aria-labelledby="web-sources-mixed-sources"] a')).toHaveLength(1);
  });

  it('does not render a Web sources section without a Web citation', async () => {
    await act(async () => root.render(
      <AssistantMessage message={{ id: 'no-web', role: 'assistant', content: 'Answer without a citation.' }} />,
    ));

    expect(container.textContent).not.toContain('Sources Web');
  });

  it('renders ordered visuals between the answer and suggested questions', async () => {
    const loadVisual = vi.fn().mockResolvedValue(new Blob(['png'], { type: 'image/png' }));
    await act(async () => root.render(
      <AssistantMessage
        message={{
          id: 'assistant-visuals', role: 'assistant',
          content: 'Réponse principale\n\nQuestions suggérées :\n- Voulez-vous poursuivre ?',
          visuals: [
            { id: 1, documentId: 'manual', page: 71, assetType: 'tile', tile: 'r02-c01', name: 'a.png', displayOrder: 0 },
            { id: 2, documentId: 'manual', page: 72, assetType: 'full', tile: null, name: 'b.png', displayOrder: 1 },
          ],
        }}
        onLoadTechnicalVisual={loadVisual}
        onSuggestedPromptClick={vi.fn()}
      />
    ));
    await act(async () => Promise.resolve());

    const article = container.querySelector('[data-testid="assistant-message"]')!;
    const cards = article.querySelectorAll('[data-technical-visual-id]');
    const question = Array.from(article.querySelectorAll('button')).find(button => button.textContent === 'Voulez-vous poursuivre ?');
    expect(Array.from(cards).map(card => card.getAttribute('data-technical-visual-id'))).toEqual(['1', '2']);
    expect(cards[0].textContent).toContain('Page 71');
    expect(cards[0].textContent).toContain('Vue détaillée');
    expect(cards[1].textContent).toContain('Page complète');
    expect(cards[1].compareDocumentPosition(question!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(loadVisual).toHaveBeenCalledTimes(2);
  });

  it('shows a neutral unavailable state when image loading fails', async () => {
    await act(async () => root.render(
      <AssistantMessage
        message={{ id: 'failed', role: 'assistant', content: 'Réponse', visuals: [
          { id: 3, documentId: 'manual', page: 73, assetType: 'full', tile: null, name: 'c.png', displayOrder: 0 },
        ] }}
        onLoadTechnicalVisual={vi.fn().mockRejectedValue(new Error('private detail'))}
      />
    ));
    await act(async () => Promise.resolve());
    expect(container.textContent).toContain('Illustration indisponible');
    expect(container.textContent).not.toContain('private detail');
  });

  it('opens and closes the enlarged visual dialog', async () => {
    await act(async () => root.render(
      <AssistantMessage
        message={{ id: 'modal', role: 'assistant', content: 'Réponse', visuals: [
          { id: 4, documentId: 'manual', page: 74, assetType: 'full', tile: null, name: 'd.png', displayOrder: 0 },
        ] }}
        onLoadTechnicalVisual={vi.fn().mockResolvedValue(new Blob(['png']))}
      />
    ));
    await act(async () => Promise.resolve());
    const enlarge = container.querySelector<HTMLButtonElement>('button[aria-label="Agrandir illustration technique — page 74"]');
    await act(async () => enlarge?.click());
    expect(document.body.querySelector('[role="dialog"]')?.textContent).toContain('Page 74');
    const close = document.body.querySelector<HTMLButtonElement>('button[aria-label="Fermer"]');
    await act(async () => close?.click());
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
  });
});
