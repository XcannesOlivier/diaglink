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
vi.mock('../FilePreview', () => ({
  FilePreview: ({ files }: { files: File[] }) => (
    <div data-testid="selected-files">{files.map(file => file.name).join(',')}</div>
  ),
}));
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

  const createFile = (name: string, type: string, size = 1024) => {
    const file = new File(['file'], name, { type });
    Object.defineProperty(file, 'size', { value: size });
    return file;
  };

  const selectedFileNames = () =>
    container.querySelector('[data-testid="selected-files"]')?.textContent ?? '';

  const selectFiles = async (files: File[]) => {
    const input = container.querySelector<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', { value: files, configurable: true });
    await act(async () => input.dispatchEvent(new Event('change', { bubbles: true })));
  };

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

  it.each([
    ['photo.png', 'image/png'],
    ['photo.jpeg', 'image/jpeg'],
  ])('allows ClaudeDirect to select %s', async (name, type) => {
    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} />,
    ));

    await selectFiles([createFile(name, type)]);

    expect(selectedFileNames()).toContain(name);
  });

  it.each([
    ['manual.pdf', 'application/pdf'],
    ['notes.txt', 'text/plain'],
    ['animation.gif', 'image/gif'],
    ['photo.webp', 'image/webp'],
  ])('rejects %s from the ClaudeDirect file picker', async (name, type) => {
    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} />,
    ));

    await selectFiles([createFile(name, type)]);

    expect(selectedFileNames()).not.toContain(name);
  });

  it('rejects a ClaudeDirect image larger than 5 MB', async () => {
    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} />,
    ));

    await selectFiles([createFile('large.png', 'image/png', 5 * 1024 * 1024 + 1)]);

    expect(selectedFileNames()).not.toContain('large.png');
  });

  it('keeps five ClaudeDirect images when a sixth is selected', async () => {
    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} />,
    ));
    const firstFive = Array.from({ length: 5 }, (_, index) =>
      createFile(`${index + 1}.png`, 'image/png'));

    await selectFiles(firstFive);
    await selectFiles([createFile('sixth.png', 'image/png')]);

    expect(selectedFileNames()).toBe('1.png,2.png,3.png,4.png,5.png');
  });

  it('applies the ClaudeDirect rule to drag and drop while retaining valid files', async () => {
    const onDroppedFilesConsumed = vi.fn();
    await act(async () => root.render(
      <ChatInput
        onSubmit={vi.fn()}
        droppedFiles={[
          createFile('dropped.png', 'image/png'),
          createFile('dropped.pdf', 'application/pdf'),
        ]}
        onDroppedFilesConsumed={onDroppedFilesConsumed}
      />,
    ));

    expect(selectedFileNames()).toBe('dropped.png');
    expect(onDroppedFilesConsumed).toHaveBeenCalledOnce();
  });

  it('applies the ClaudeDirect rule to clipboard files', async () => {
    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} />,
    ));
    const pasteEvent = new Event('paste', { bubbles: true, cancelable: true });
    Object.defineProperty(pasteEvent, 'clipboardData', {
      value: {
        items: [
          { kind: 'file', getAsFile: () => createFile('pasted.jpeg', 'image/jpeg') },
          { kind: 'file', getAsFile: () => createFile('pasted.webp', 'image/webp') },
        ],
      },
    });

    await act(async () =>
      container.querySelector<HTMLInputElement>('input[type="file"]')?.parentElement?.dispatchEvent(pasteEvent));

    expect(selectedFileNames()).toBe('pasted.jpeg');
  });

  it('does not remove a valid ClaudeDirect image when an invalid file is presented', async () => {
    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} />,
    ));
    await selectFiles([createFile('valid.png', 'image/png')]);

    await selectFiles([createFile('invalid.pdf', 'application/pdf')]);

    expect(selectedFileNames()).toBe('valid.png');
  });

  it('uses the fixed ClaudeDirect accept list', async () => {
    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} />,
    ));
    expect(container.querySelector<HTMLInputElement>('input[type="file"]')?.accept)
      .toBe('image/png,image/jpeg,image/jpg');
    expect(container.querySelector<HTMLButtonElement>('button[aria-label="Joindre un fichier"]')?.disabled)
      .toBe(false);
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
