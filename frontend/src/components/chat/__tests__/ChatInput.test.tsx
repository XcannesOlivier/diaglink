import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatInput } from '../ChatInput';

vi.mock('@fluentui-copilot/react-copilot', () => ({
  ChatInput: ({ children, onSubmit }: { children?: React.ReactNode; onSubmit?: () => void }) => (
    <div>
      {children}
      <button type="button" data-testid="submit-chat" onClick={onSubmit}>Envoyer</button>
    </div>
  ),
  ImperativeControlPlugin: () => null,
}));
vi.mock('../FilePreview', () => ({
  FilePreview: ({ files }: { files: File[] }) => (
    <div
      data-testid="selected-files"
      data-file-details={JSON.stringify(files.map(file => ({
        name: file.name,
        type: file.type,
        size: file.size,
      })))}
    >{files.map(file => file.name).join(',')}</div>
  ),
}));
vi.mock('../MachineDocuments', () => ({
  MachineDocuments: () => <button>Documents machine</button>,
}));
vi.mock('../VoiceInput', () => ({ VoiceInput: ({ disabled }: { disabled?: boolean }) => <button aria-label="Saisie vocale" disabled={disabled} /> }));
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
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
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

  it('keeps machine documents without rendering the change-machine button', async () => {
    await act(async () => root.render(
      <ChatInput
        onSubmit={vi.fn()}
        onChangeMachine={vi.fn()}
        machineId="machine-42"
      />,
    ));

    expect(container.textContent).toContain('Documents machine');
    expect(container.textContent).not.toContain('Changer de machine');
    const buttons = [...container.querySelectorAll('button')];
    expect(buttons.some(button => button.textContent === 'Machines')).toBe(false);
    expect(buttons.findIndex(button => button.textContent === 'Documents machine'))
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
    ['desktop', false],
    ['mobile', true],
  ])('shows the stop-response button only while streaming on %s', async (_viewport, mobile) => {
    vi.spyOn(window, 'matchMedia').mockImplementation(query => ({
      matches: mobile && query === '(max-width: 1000px)',
      media: query,
      onchange: null,
      addListener() {},
      removeListener() {},
      addEventListener() {},
      removeEventListener() {},
      dispatchEvent() { return true; },
    }));
    const onCancelStream = vi.fn();

    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} isStreaming onCancelStream={onCancelStream} />,
    ));

    const stopButton = container.querySelector<HTMLButtonElement>('[aria-label="Arrêter la réponse"]');
    expect(stopButton).not.toBeNull();
    await act(async () => stopButton!.click());
    expect(onCancelStream).toHaveBeenCalledOnce();

    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} isStreaming={false} onCancelStream={onCancelStream} />,
    ));
    expect(container.querySelector('[aria-label="Arrêter la réponse"]')).toBeNull();
  });

  it('keeps Escape wired to the existing stream cancellation callback', async () => {
    const onCancelStream = vi.fn();
    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} isStreaming onCancelStream={onCancelStream} />,
    ));

    await act(async () => {
      container.querySelector<HTMLInputElement>('input[type="file"]')?.parentElement?.dispatchEvent(new KeyboardEvent('keydown', {
        key: 'Escape',
        bubbles: true,
        cancelable: true,
      }));
    });

    expect(onCancelStream).toHaveBeenCalledOnce();
  });

  it('groups composition and conversation actions while retaining their callbacks', async () => {
    const onNewChat = vi.fn();
    const onToggleSidebar = vi.fn();
    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} hasMessages onNewChat={onNewChat} onToggleSidebar={onToggleSidebar} />,
    ));
    const compose = container.querySelector('[role="group"][aria-label="Pièces jointes et saisie vocale"]');
    const conversations = container.querySelector('[role="group"][aria-label="Conversations"]');
    expect(compose?.querySelector('[aria-label="Joindre un fichier"]')).not.toBeNull();
    expect(compose?.querySelector('[aria-label="Saisie vocale"]')).not.toBeNull();
    expect(conversations?.querySelectorAll('button')).toHaveLength(2);
    expect(compose?.contains(conversations)).toBe(false);
    await act(async () => {
      conversations?.querySelector<HTMLButtonElement>('[aria-label="Nouvelle conversation"]')?.click();
      conversations?.querySelector<HTMLButtonElement>('[aria-label="Historique des conversations"]')?.click();
    });
    expect(onNewChat).toHaveBeenCalledOnce();
    expect(onToggleSidebar).toHaveBeenCalledOnce();
  });

  it('keeps disabled actions disabled and hides the conversation group when no callback exists', async () => {
    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} disabled hasMessages onNewChat={vi.fn()} onToggleSidebar={vi.fn()} />,
    ));
    for (const label of ['Joindre un fichier', 'Saisie vocale', 'Nouvelle conversation', 'Historique des conversations']) {
      expect(container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)?.disabled).toBe(true);
    }
    await act(async () => root.render(<ChatInput onSubmit={vi.fn()} />));
    expect(container.querySelector('[role="group"][aria-label="Conversations"]')).toBeNull();
  });

  it('retains the new-chat guard when there are no messages', async () => {
    await act(async () => root.render(<ChatInput onSubmit={vi.fn()} onNewChat={vi.fn()} />));
    expect(container.querySelector<HTMLButtonElement>('[aria-label="Nouvelle conversation"]')?.disabled).toBe(true);
  });

  it('uses comfortable action targets and keeps conversation buttons together when wrapping', () => {
    const css = readFileSync(resolve('src/components/chat/ChatInput.module.css'), 'utf8');
    expect(css).toMatch(/\.actionGroup > button,[\s\S]*?min-width:\s*40px;[\s\S]*?min-height:\s*40px;/);
    expect(css).toMatch(/\.actionGroup\s*\{[^}]*display:\s*flex;[^}]*flex-shrink:\s*0;/);
    expect(css).toMatch(/\.conversationActions\s*\{[^}]*border-left:\s*1px solid var\(--colorNeutralStroke2\);/);
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

  it.each([
    ['desktop', false, [
      { fileName: 'photo.png', fileSizeBytes: 3, dataUri: 'data:image/png;base64,AQID' },
    ]],
    ['mobile with five ordered photos', true, [
      { fileName: '01.png', fileSizeBytes: 1, dataUri: 'data:image/png;base64,AQ==' },
      { fileName: '02.jpg', fileSizeBytes: 2, dataUri: 'data:image/jpeg;base64,AQI=' },
      { fileName: '03.jpeg', fileSizeBytes: 3, dataUri: 'data:image/jpeg;base64,AQID' },
      { fileName: '04.png', fileSizeBytes: 4, dataUri: 'data:image/png;base64,AQIDBA==' },
      { fileName: '05.jpg', fileSizeBytes: 5, dataUri: 'data:image/jpeg;base64,AQIDBAU=' },
    ]],
  ])('restores 402 text and photos on %s before consuming recovery', async (_scenario, mobile, attachments) => {
    vi.stubGlobal('fetch', vi.fn(async (input: string | URL | Request) => {
      const dataUri = String(input);
      const [header, encoded] = dataUri.split(',', 2);
      const mimeType = header.match(/^data:([^;]+)/)?.[1] ?? 'application/octet-stream';
      const bytes = Uint8Array.from(atob(encoded), character => character.charCodeAt(0));
      return { blob: async () => new Blob([bytes], { type: mimeType }) } as Response;
    }));
    vi.spyOn(window, 'matchMedia').mockImplementation(query => ({
      matches: mobile && query === '(max-width: 1000px)',
      media: query,
      onchange: null,
      addListener() {},
      removeListener() {},
      addEventListener() {},
      removeEventListener() {},
      dispatchEvent() { return true; },
    }));
    const onSubmit = vi.fn();
    const onRecoveredInputConsumed = vi.fn();

    await act(async () => root.render(
      <ChatInput
        onSubmit={onSubmit}
        recoveredInput="Question restaurÃ©e"
        recoveredAttachments={attachments}
        onRecoveredInputConsumed={onRecoveredInputConsumed}
      />,
    ));

    await vi.waitFor(() => expect(selectedFileNames())
      .toBe(attachments.map(attachment => attachment.fileName).join(',')));
    const details = JSON.parse(
      container.querySelector('[data-testid="selected-files"]')?.getAttribute('data-file-details') ?? '[]',
    );
    expect(details).toEqual(attachments.map(attachment => ({
      name: attachment.fileName,
      type: attachment.dataUri.split(';')[0].slice(5),
      size: attachment.fileSizeBytes,
    })));
    expect(onRecoveredInputConsumed).toHaveBeenCalledOnce();

    await act(async () => container.querySelector<HTMLButtonElement>('[data-testid="submit-chat"]')?.click());
    expect(onSubmit).toHaveBeenCalledWith(
      'Question restaurÃ©e',
      expect.arrayContaining(attachments.map(attachment => expect.objectContaining({ name: attachment.fileName }))),
    );
  });

  it('ignores an unfinished recovery after another conversation is loaded', async () => {
    let resolveFetch!: (response: Response) => void;
    const pendingFetch = new Promise<Response>(resolve => { resolveFetch = resolve; });
    vi.stubGlobal('fetch', vi.fn(() => pendingFetch));
    const onRecoveredInputConsumed = vi.fn();
    const attachment = {
      fileName: 'conversation-a.png',
      fileSizeBytes: 1,
      dataUri: 'data:image/png;base64,AQ==',
    };

    await act(async () => root.render(
      <ChatInput
        onSubmit={vi.fn()}
        currentConversationId="conversation-a"
        recoveredInput="Question from A"
        recoveredAttachments={[attachment]}
        onRecoveredInputConsumed={onRecoveredInputConsumed}
      />,
    ));

    await act(async () => root.render(
      <ChatInput
        onSubmit={vi.fn()}
        currentConversationId="conversation-b"
        onRecoveredInputConsumed={onRecoveredInputConsumed}
      />,
    ));
    await act(async () => resolveFetch({
      blob: async () => new Blob([new Uint8Array([1])], { type: 'image/png' }),
    } as Response));

    expect(selectedFileNames()).toBe('');
    expect(onRecoveredInputConsumed).not.toHaveBeenCalled();
  });

  it('keeps an ordinary selected file when the conversation changes', async () => {
    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} currentConversationId="conversation-a" />,
    ));
    const input = container.querySelector<HTMLInputElement>('input[type="file"]')!;
    const ordinaryFile = createFile('ordinary-draft.png', 'image/png');
    Object.defineProperty(input, 'files', { configurable: true, value: [ordinaryFile] });
    await act(async () => input.dispatchEvent(new Event('change', { bubbles: true })));
    expect(selectedFileNames()).toBe('ordinary-draft.png');

    await act(async () => root.render(
      <ChatInput onSubmit={vi.fn()} currentConversationId="conversation-b" />,
    ));

    expect(selectedFileNames()).toBe('ordinary-draft.png');
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
