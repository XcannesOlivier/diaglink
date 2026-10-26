import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PublicHeader } from '../PublicHeader';
import { APP_INSTALL_URL, APP_LOGIN_URL } from '../../../config/origins';

function Location() {
  const location = useLocation();
  return <span data-location>{location.pathname}{location.hash}</span>;
}

describe('PublicHeader install shortcut', () => {
  let root: Root;
  let container: HTMLDivElement;
  beforeEach(async () => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    vi.stubGlobal('scrollTo', vi.fn());
  });
  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
    vi.clearAllMocks();
  });

  const installButton = () => [...container.querySelectorAll('button')]
    .find(button => button.textContent === 'Installer DiagLink') as HTMLButtonElement;
  async function render(isAuthenticated: boolean, initialPath = '/', landingPath?: string) {
    await act(async () => root.render(<MemoryRouter initialEntries={[initialPath]}><PublicHeader loginTarget={APP_LOGIN_URL}
      isAuthenticated={isAuthenticated} landingPath={landingPath} /><Location /></MemoryRouter>));
  }

  const homeLink = () => container.querySelector<HTMLAnchorElement>('[aria-label="DiagLink — accueil"]')!;

  it('routes the logo from a secondary page to the landing page and scrolls to the top', async () => {
    await render(false, '/contact', '/');

    expect(homeLink().getAttribute('href')).toBe('/');
    await act(async () => homeLink().click());

    expect(container.querySelector('[data-location]')?.textContent).toBe('/');
    expect(window.scrollTo).toHaveBeenCalledExactlyOnceWith({ top: 0, left: 0, behavior: 'auto' });
  });

  it('scrolls the landing page to the top when its logo is clicked', async () => {
    await render(false);

    await act(async () => homeLink().click());

    expect(container.querySelector('[data-location]')?.textContent).toBe('/');
    expect(window.scrollTo).toHaveBeenCalledExactlyOnceWith({ top: 0, left: 0, behavior: 'auto' });
  });

  it('keeps the same home link available when the mobile menu is open', async () => {
    await render(false, '/confidentialite', '/');
    await act(async () => container.querySelector<HTMLButtonElement>('[aria-label="Ouvrir le menu"]')!.click());

    expect(homeLink().getAttribute('href')).toBe('/');
    expect(container.querySelector('[aria-label="Navigation mobile"]')).not.toBeNull();
    await act(async () => homeLink().click());

    expect(container.querySelector('[data-location]')?.textContent).toBe('/');
    expect(window.scrollTo).toHaveBeenCalledExactlyOnceWith({ top: 0, left: 0, behavior: 'auto' });
  });

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
