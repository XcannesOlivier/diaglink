import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatInterface } from '../ChatInterface';
import styles from '../ChatInterface.module.css';

vi.mock('../animations/Waves', () => ({ Waves: () => null }));
vi.mock('../chat/ChatInput', () => ({ ChatInput: () => <div data-chat-input /> }));
vi.mock('../chat/DropZone', () => ({ DropZone: () => null }));
vi.mock('../chat/StarterMessages', () => ({ StarterMessages: ({accessory, agentName}:{accessory?:ReactNode; agentName?:string}) => <><span data-agent-name>{agentName}</span>{accessory}</> }));
vi.mock('../chat/UserMessage', () => ({ UserMessage: () => <div data-user-message /> }));
vi.mock('../chat/AssistantMessage', () => ({ AssistantMessage: () => <div data-assistant-message /> }));
vi.mock('../core/BuiltWithBadge', () => ({
  BuiltWithBadge: ({ className }: { className?: string }) => (
    <button className={className}>Propulsé par Microsoft Foundry</button>
  ),
}));

describe('ChatInterface branding', () => {
  let container: HTMLDivElement;
  let root: Root;
  const originalScrollIntoView = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollIntoView');

  beforeEach(() => {
    Object.defineProperty(Element.prototype, 'scrollIntoView', { configurable: true, value: vi.fn() });
    vi.stubGlobal('ResizeObserver', class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal('IntersectionObserver', class {
      observe() {}
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
    if (originalScrollIntoView) {
      Object.defineProperty(Element.prototype, 'scrollIntoView', originalScrollIntoView);
    } else {
      Reflect.deleteProperty(Element.prototype, 'scrollIntoView');
    }
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

  const machine = { id: 'm1', name: 'Compresseur Alpha', reference: 'CMP-2026' };
  const renderMachineContext = async (selectedMachine: typeof machine | null, options: { withMessages?: boolean; showPrompt?: boolean; onChangeMachine?: () => void } = {}) => {
    await act(async () => root.render(
      <ChatInterface
        messages={options.withMessages ? [{ id: 'u1', role: 'user', content: 'Question' }] : []}
        status="idle"
        error={null}
        onSendMessage={vi.fn()}
        disabled={false}
        selectedMachine={selectedMachine}
        agentName="Assistant Technique — Compresseur Alpha"
        showMachineSelectionPrompt={options.showPrompt}
        onChangeMachine={options.onChangeMachine}
      />,
    ));
  };

  it('identifies the active machine and its reference outside the scrolling messages', async () => {
    await renderMachineContext(machine);
    const context = container.querySelector('[aria-label="Machine active"]');
    expect(context?.textContent).toContain(machine.name);
    expect(context?.textContent).toContain(`Réf. ${machine.reference}`);
    expect(container.querySelector('[role="log"]')?.contains(context)).toBe(false);
    expect(container.querySelector('[data-agent-name]')?.textContent).toBe('Assistant Technique');
    expect(container.querySelector('[aria-label="Sélection d’une machine"]')).toBeNull();
  });

  it('keeps the machine context visible when messages exist and updates it when the selection changes', async () => {
    await renderMachineContext(machine, { withMessages: true });
    expect(container.querySelector('[data-user-message]')).not.toBeNull();
    expect(container.querySelector('[aria-label="Machine active"]')?.textContent).toContain(machine.name);
    await renderMachineContext({ id: 'm2', name: 'Presse Beta', reference: '' }, { withMessages: true });
    const context = container.querySelector('[aria-label="Machine active"]');
    expect(context?.textContent).toContain('Presse Beta');
    expect(context?.textContent).not.toContain(machine.name);
    expect(context?.textContent).not.toContain('Réf.');
  });

  it('explains the empty state and uses the existing machine-selection callback', async () => {
    const onChangeMachine = vi.fn();
    await renderMachineContext(null, { showPrompt: true, onChangeMachine });
    expect(container.querySelector('[aria-label="Machine active"]')).toBeNull();
    const prompt = container.querySelector('[aria-label="Sélection d’une machine"]');
    expect(prompt?.textContent).toContain('Choisissez une machine pour commencer');
    expect(prompt?.textContent).toContain('consulter ses documents');
    await act(async () => prompt?.querySelector<HTMLButtonElement>('button')?.click());
    expect(onChangeMachine).toHaveBeenCalledOnce();
  });

  it('does not show an empty-state instruction for a legacy conversation or during a conversation', async () => {
    await renderMachineContext(null, { showPrompt: false });
    expect(container.querySelector('[aria-label="Sélection d’une machine"]')).toBeNull();
    await renderMachineContext(null, { showPrompt: true, withMessages: true });
    expect(container.querySelector('[aria-label="Sélection d’une machine"]')).toBeNull();
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
