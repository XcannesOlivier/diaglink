import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PublicHeader } from '../PublicHeader';
import { APP_INSTALL_URL, APP_LOGIN_URL } from '../../../config/origins';

function Location() {
  return <span data-location>{useLocation().pathname}</span>;
}

describe('PublicHeader install shortcut', () => {
  let root: Root;
  let container: HTMLDivElement;
  beforeEach(async () => {
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
  async function render(isAuthenticated: boolean) {
    await act(async () => root.render(<MemoryRouter><PublicHeader loginTarget={APP_LOGIN_URL}
      isAuthenticated={isAuthenticated} /><Location /></MemoryRouter>));
  }

  it.each([false, true])('opens the login installation route in a new tab when authenticated is %s', async isAuthenticated => {
    const open = vi.spyOn(window, 'open').mockImplementation(() => null);
    await render(isAuthenticated);
    await act(async () => installButton().click());
    expect(container.querySelector('[data-location]')?.textContent).toBe('/');
    expect(document.body.textContent).not.toContain('Créer un raccourci DiagLink');
    expect(open).toHaveBeenCalledWith(APP_INSTALL_URL, '_blank', 'noopener,noreferrer');
  });

  it('routes desktop and mobile primary actions to the existing machine setup flow', async () => {
    await render(false);
    const setupLinks = () => [...container.querySelectorAll('a')]
      .filter(link => link.textContent === 'Configurer ma première machine');
    const loginLinks = () => [...container.querySelectorAll('a')]
      .filter(link => link.textContent === 'Se connecter');
    expect(setupLinks()).toHaveLength(1);
    expect(setupLinks()[0].getAttribute('href')).toBe('/commencer');
    expect(loginLinks()).toHaveLength(0);
    expect(container.textContent).not.toContain('Voir une démonstration');

    await act(async () => container.querySelector<HTMLButtonElement>('[aria-label="Ouvrir le menu"]')!.click());
    expect(setupLinks()).toHaveLength(2);
    expect(setupLinks().every(link => link.getAttribute('href') === '/commencer')).toBe(true);
    expect(loginLinks()).toHaveLength(1);
    expect(loginLinks()[0].getAttribute('href')).toBe(APP_LOGIN_URL);
    expect(loginLinks()[0].getAttribute('target')).toBe('_blank');
    expect(loginLinks()[0].getAttribute('rel')).toBe('noopener noreferrer');
    await act(async () => setupLinks()[1].click());
    expect(container.querySelector('[data-location]')?.textContent).toBe('/commencer');
    expect(container.querySelector('[aria-label="Navigation mobile"]')).toBeNull();
  });

  it('uses the same new-tab installation route from the mobile menu', async () => {
    const open = vi.spyOn(window, 'open').mockImplementation(() => null);
    await render(false);
    await act(async () => container.querySelector<HTMLButtonElement>('[aria-label="Ouvrir le menu"]')!.click());
    const mobileInstallButton = [...container.querySelectorAll('button')]
      .find(button => button.textContent === 'Installer DiagLink');
    await act(async () => mobileInstallButton?.click());
    expect(open).toHaveBeenCalledWith(APP_INSTALL_URL, '_blank', 'noopener,noreferrer');
    expect(container.querySelector('[aria-label="Navigation mobile"]')).toBeNull();
  });
});
