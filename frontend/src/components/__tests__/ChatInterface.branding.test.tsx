import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatInterface } from '../ChatInterface';
import styles from '../ChatInterface.module.css';

vi.mock('../animations/Waves', () => ({ Waves: () => null }));
vi.mock('../chat/ChatInput', () => ({ ChatInput: () => <div data-chat-input /> }));
vi.mock('../chat/DropZone', () => ({ DropZone: () => null }));
vi.mock('../chat/StarterMessages', () => ({ StarterMessages: ({accessory}:{accessory?:ReactNode}) => <>{accessory}</> }));
vi.mock('../core/BuiltWithBadge', () => ({
  BuiltWithBadge: ({ className }: { className?: string }) => (
    <button className={className}>Propulsé par Microsoft Foundry</button>
  ),
}));

describe('ChatInterface branding', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    vi.stubGlobal('ResizeObserver', class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
  });

  it('renders the Foundry badge above the DiagLink logo in the dedicated footer block', async () => {
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
    expect(branding?.children[0]).toBe(badge);
    expect(branding?.children[1]).toBe(logo);
    expect(badge?.textContent).toBe('Propulsé par Microsoft Foundry');
  });

  it('renders the machine confirmation accessory in the empty-chat content area',async()=>{
    await act(async()=>root.render(<ChatInterface messages={[]} status="idle" error={null} onSendMessage={vi.fn()} disabled={false} starterAccessory={<div data-machine-confirmation/>}/>));
    expect(container.querySelector('[data-machine-confirmation]')).not.toBeNull();
  });

  it('renders exhausted AI credit as a warning while preserving its message', async () => {
    const message = 'Crédit IA épuisé. Rechargez le portefeuille de votre entreprise pour continuer. L’historique reste accessible.';
    await act(async () => root.render(
      <ChatInterface
        messages={[]}
        status="error"
        error={{ code: 'AiCreditExhausted', message, recoverable: false }}
        onSendMessage={vi.fn()}
        disabled={false}
      />,
    ));

    const alert = container.querySelector<HTMLElement>('[role="alert"]');
    expect(alert?.dataset.intent).toBe('warning');
    expect(alert?.textContent).toContain(message);
  });

  it('keeps technical errors in the error style', async () => {
    await act(async () => root.render(
      <ChatInterface
        messages={[]}
        status="error"
        error={{ code: 'NETWORK', message: 'Erreur réseau', recoverable: true }}
        onSendMessage={vi.fn()}
        disabled={false}
      />,
    ));

    expect(container.querySelector<HTMLElement>('[role="alert"]')?.dataset.intent).toBe('error');
  });
});
