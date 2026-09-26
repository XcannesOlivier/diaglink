import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatInterface } from '../ChatInterface';
import styles from '../ChatInterface.module.css';

vi.mock('../animations/Waves', () => ({ Waves: () => null }));
vi.mock('../chat/ChatInput', () => ({ ChatInput: () => <div data-chat-input /> }));
vi.mock('../chat/DropZone', () => ({ DropZone: () => null }));
vi.mock('../chat/StarterMessages', () => ({ StarterMessages: () => null }));
vi.mock('../core/BuiltWithBadge', () => ({
  BuiltWithBadge: ({ className }: { className?: string }) => (
    <button className={className}>Propulsé par Microsoft Foundry</button>
  ),
}));

describe('ChatInterface branding', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
  });

  it('renders the DiagLink logo above the Foundry badge in the dedicated footer block', async () => {
    await act(async () => root.render(
      <ChatInterface
        messages={[]}
        status="idle"
        error={null}
        onSendMessage={vi.fn()}
        disabled={false}
      />,
    ));

    const logo = container.querySelector<HTMLImageElement>('img[alt="DiagLink"]');
    const branding = logo?.parentElement;
    const badge = branding?.querySelector('button');

    expect(branding?.classList.contains(styles.poweredRow)).toBe(true);
    expect(logo?.classList.contains(styles.diagLinkLogo)).toBe(true);
    expect(branding?.children[0]).toBe(logo);
    expect(branding?.children[1]).toBe(badge);
    expect(badge?.textContent).toBe('Propulsé par Microsoft Foundry');
  });
});