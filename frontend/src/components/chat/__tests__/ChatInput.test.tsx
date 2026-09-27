import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
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
    expect([...container.querySelectorAll('button')]
      .some(button => button.textContent === 'Machines')).toBe(false);
  });
});
