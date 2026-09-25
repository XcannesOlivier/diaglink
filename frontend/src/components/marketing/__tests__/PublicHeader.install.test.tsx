import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PublicHeader } from '../PublicHeader';

const platform = vi.fn();
vi.mock('../../../utils/installShortcut', async importOriginal => ({
  ...await importOriginal<typeof import('../../../utils/installShortcut')>(),
  detectShortcutPlatform: () => platform(),
}));

function Location() {
  return <span data-location>{useLocation().pathname}</span>;
}

describe('PublicHeader install shortcut', () => {
  let root: Root;
  let container: HTMLDivElement;
  beforeEach(async () => {
    sessionStorage.clear();
    platform.mockReturnValue('windows');
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });
  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    vi.clearAllMocks();
  });

  const installButton = () => [...container.querySelectorAll('button')]
    .find(button => button.textContent === 'Installer DiagLink') as HTMLButtonElement;
  const dialogButton = (label: string) => [...document.body.querySelectorAll('button')]
    .find(button => button.textContent === label) as HTMLButtonElement;
  async function render(isAuthenticated: boolean) {
    await act(async () => root.render(<MemoryRouter><PublicHeader loginTarget={isAuthenticated ? '/app' : '/login'}
      isAuthenticated={isAuthenticated} /><Location /></MemoryRouter>));
  }

  it('shows an explanation before navigating an unauthenticated user', async () => {
    await render(false);
    await act(async () => installButton().click());
    expect(container.querySelector('[data-location]')?.textContent).toBe('/');
    expect(document.body.textContent).toContain('connectez-vous d’abord à votre espace');
    expect(sessionStorage.getItem('diaglink.pendingInstallPlatform')).toBeNull();
  });

  it('cancels without navigating or preserving an installation intent', async () => {
    await render(false);
    await act(async () => installButton().click());
    await act(async () => dialogButton('Annuler').click());
    expect(container.querySelector('[data-location]')?.textContent).toBe('/');
    expect(sessionStorage.getItem('diaglink.pendingInstallPlatform')).toBeNull();
  });

  it('starts the existing login route and preserves the installation intent after confirmation', async () => {
    await render(false);
    await act(async () => installButton().click());
    await act(async () => dialogButton('Se connecter et continuer').click());
    expect(container.querySelector('[data-location]')?.textContent).toBe('/login');
    expect(sessionStorage.getItem('diaglink.pendingInstallPlatform')).toBe('windows');
  });

  it.each([
    ['windows', 'windows'],
    ['ios', 'ios'],
    ['android', 'android'],
    ['other', 'other'],
  ])('navigates authenticated %s users directly to the chat route', async (value, stored) => {
    platform.mockReturnValue(value);
    await render(true);
    await act(async () => installButton().click());
    expect(container.querySelector('[data-location]')?.textContent).toBe('/app');
    expect(sessionStorage.getItem('diaglink.pendingInstallPlatform')).toBe(stored);
  });
});
