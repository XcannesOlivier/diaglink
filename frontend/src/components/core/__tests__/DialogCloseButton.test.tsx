import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DialogCloseButton } from '../DialogCloseButton';

describe('DialogCloseButton', () => {
  const hosts: HTMLDivElement[] = [];

  afterEach(async () => {
    for (const host of hosts.splice(0)) {
      host.remove();
    }
  });

  it('expose un libellé accessible et exécute la fermeture', async () => {
    const host = document.createElement('div');
    hosts.push(host);
    document.body.appendChild(host);
    const root = createRoot(host);
    const onClick = vi.fn();

    await act(async () => root.render(<DialogCloseButton onClick={onClick} />));
    const button = host.querySelector<HTMLButtonElement>('button[aria-label="Fermer"]');

    expect(button).not.toBeNull();
    expect(button?.type).toBe('button');
    expect(button?.hasAttribute('data-dialog-close-button')).toBe(true);
    await act(async () => button?.click());
    expect(onClick).toHaveBeenCalledOnce();
    await act(async () => root.unmount());
  });

  it('reste inactif pendant une opération protégée', async () => {
    const host = document.createElement('div');
    hosts.push(host);
    document.body.appendChild(host);
    const root = createRoot(host);
    const onClick = vi.fn();

    await act(async () => root.render(<DialogCloseButton disabled onClick={onClick} />));
    const button = host.querySelector<HTMLButtonElement>('button[aria-label="Fermer"]');

    expect(button?.disabled).toBe(true);
    await act(async () => button?.click());
    expect(onClick).not.toHaveBeenCalled();
    await act(async () => root.unmount());
  });
});
