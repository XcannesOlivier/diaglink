import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatInput } from '../ChatInput';

vi.mock('@fluentui-copilot/react-copilot', () => ({
  ChatInput: ({ children }: { children?: React.ReactNode }) => <div>{children}</div>,
  ImperativeControlPlugin: () => null,
}));
vi.mock('../FilePreview', () => ({ FilePreview: () => null }));
vi.mock('../MachineDocuments', () => ({
  MachineDocuments: () => <button>Doc Machines</button>,
}));
vi.mock('../VoiceInput', () => ({ VoiceInput: () => <button aria-label="Saisie vocale" /> }));
vi.mock('../MessageQueue', () => ({ MessageQueue: () => null }));

describe('ChatInput machine actions', () => {
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

  it('keeps Doc Machines without rendering the change-machine button', async () => {
    await act(async () => root.render(
      <ChatInput
        onSubmit={vi.fn()}
        onChangeMachine={vi.fn()}
        machineId="machine-42"
      />,
    ));

    expect(container.textContent).toContain('Doc Machines');
    expect(container.textContent).not.toContain('Changer de machine');
    const buttons = [...container.querySelectorAll('button')];
    expect(buttons.some(button => button.textContent === 'Machines')).toBe(false);
    expect(buttons.findIndex(button => button.textContent === 'Doc Machines'))
      .toBeLessThan(buttons.findIndex(button => button.getAttribute('aria-label') === 'Joindre un fichier'));
  });

  it('keeps the mobile menu action separate without changing its behavior', async () => {
    const onOpenMobileMenu = vi.fn();
    await act(async () => root.render(
      <ChatInput
        onSubmit={vi.fn()}
        onOpenMobileMenu={onOpenMobileMenu}
        onNewChat={vi.fn()}
        onToggleSidebar={vi.fn()}
        machineId="machine-42"
      />,
    ));

    const menuContainer = container.querySelector('[data-mobile-menu-container]');
    const menuButton = menuContainer?.querySelector<HTMLButtonElement>('button[aria-label="Ouvrir le menu"]');
    expect(menuContainer).not.toBeNull();
    expect(menuButton).not.toBeNull();
    await act(async () => menuButton!.click());
    expect(onOpenMobileMenu).toHaveBeenCalledOnce();
  });

  it('pushes only the burger to the right below the smartphone breakpoint', () => {
    const css = readFileSync(resolve('src/components/chat/ChatInput.module.css'), 'utf8');
    const smartphoneRules = css.slice(css.indexOf('@media (max-width: 480px)'), css.indexOf('.divider'));
    expect(smartphoneRules).toContain('flex-wrap: nowrap');
    expect(smartphoneRules).toContain('flex-wrap: wrap');
    expect(smartphoneRules).toMatch(/\.mobileMenuContainer\s*\{[\s\S]*?margin-left:\s*auto;/);
    expect(smartphoneRules).not.toContain('position: absolute');
  });
});
