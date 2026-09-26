import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { InstallShortcutDialog } from '../InstallShortcutDialog';
import type { ShortcutBrowser, ShortcutPlatform } from '../../../utils/installShortcut';

describe('InstallShortcutDialog', () => {
  let root: Root | null = null;
  let container: HTMLDivElement | null = null;

  afterEach(async () => {
    if (root) await act(async () => root?.unmount());
    container?.remove();
    root = null;
    container = null;
  });

  async function render(platform: ShortcutPlatform, browser: ShortcutBrowser = 'other') {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    await act(async () => root?.render(<InstallShortcutDialog platform={platform} browser={browser} onClose={vi.fn()} />));
    return document.body.textContent ?? '';
  }

  async function renderOnLogin(platform: ShortcutPlatform, browser: ShortcutBrowser = 'other') {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    await act(async () => root?.render(
      <InstallShortcutDialog platform={platform} browser={browser} showOpenLoginButton={false} onClose={vi.fn()} />,
    ));
    return document.body.textContent ?? '';
  }

  it('shows Edge desktop shortcut instructions on Windows', async () => {
    const text = await render('windows', 'edge');
    expect(text).toContain('Dans Microsoft Edge');
    expect(text).toContain('Cliquez sur les 3 points (…) en haut à droite de Microsoft Edge');
    expect(text).toContain('Cliquez sur « Outils supplémentaires »');
    expect(text).toContain('Cliquez sur « Applications »');
    expect(text).toContain('Cliquez sur « Installer ce site en tant qu’application »');
    expect(text).toContain('remplacez le nom proposé par « DiagLink »');
    expect(text).toContain('Cochez « Créer un raccourci sur le bureau »');
    expect(text).toContain('Cliquez sur « Autoriser »');
    expect(text).toContain('Une icône DiagLink sera ajoutée sur votre bureau et ouvrira directement la page de connexion.');
    expect(text).toContain('Vous pouvez aussi épingler DiagLink à la barre des tâches.');
  });

  it('shows Chrome shortcut instructions without offering an app installation', async () => {
    const text = await render('windows', 'chrome');
    expect(text).toContain('Dans Google Chrome');
    expect(text).toContain('Choisissez « Créer un raccourci »');
    expect(text).toContain('laissez « Ouvrir dans une fenêtre » désactivée');
    expect(text).not.toContain('Installer la page en tant qu’application');
  });

  it('shows macOS instructions that keep the shortcut in the browser', async () => {
    const text = await render('macos', 'safari');
    expect(text).toContain('Dans Safari');
    expect(text).toContain('Faites glisser l’adresse');
    expect(text).not.toContain('Ajouter au Dock');
  });

  it('shows the complete Safari shortcut instructions on iPhone and iPad', async () => {
    const text = await render('ios', 'safari');
    expect(text).toContain('Touchez le bouton Partager');
    expect(text).toContain('désactivez « Ouvrir comme app web »');
    expect(text).toContain('Touchez « Ajouter »');
  });

  it('shows a Safari fallback for other iPhone and iPad browsers', async () => {
    const text = await render('ios', 'chrome');
    expect(text).toContain('menu Partager de votre navigateur');
    expect(text).toContain('ouvrez la page dans Safari');
  });

  it('shows Android home-screen shortcut instructions', async () => {
    const text = await render('android', 'chrome');
    expect(text).toContain('Ajouter à l’écran d’accueil');
    expect(text).toContain('Choisissez uniquement « Créer un raccourci »');
  });

  it('opens the login page in a new tab', async () => {
    const open = vi.spyOn(window, 'open').mockImplementation(() => null);
    await render('windows', 'edge');
    const openButton = [...document.body.querySelectorAll('button')]
      .find(button => button.textContent === 'Ouvrir DiagLink');
    await act(async () => openButton?.click());
    expect(open).toHaveBeenCalledWith('/login', '_blank', 'noopener,noreferrer');
  });

  it('uses the current login page without offering another navigation', async () => {
    const text = await renderOnLogin('windows', 'edge');
    expect(text).toContain('Créez maintenant un raccourci vers cette page de connexion.');
    expect(text).not.toContain('Cliquez sur « Ouvrir DiagLink »');
    expect([...document.body.querySelectorAll('button')]
      .some(button => button.textContent === 'Ouvrir DiagLink')).toBe(false);
  });
});
