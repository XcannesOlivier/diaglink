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

  it('shows Edge instructions on Windows without offering a download', async () => {
    const text = await render('windows', 'edge');
    expect(text).toContain('Dans Microsoft Edge');
    expect(text).toContain('Installer ce site en tant qu’application');
    expect(text).not.toContain('téléchargé');
    expect(text).not.toContain('DiagLink.url');
  });

  it('shows version-tolerant Chrome instructions on Windows', async () => {
    const text = await render('windows', 'chrome');
    expect(text).toContain('Dans Google Chrome');
    expect(text).toContain('selon la version de Chrome');
  });

  it('shows the complete Safari instructions on iPhone and iPad', async () => {
    const text = await render('ios');
    expect(text).toContain('Ouvrez cette page dans Safari');
    expect(text).toContain('Chrome, Edge ou un navigateur intégré');
    expect(text).toContain('Touchez « Ajouter »');
  });

  it('shows the complete Chrome instructions on Android', async () => {
    const text = await render('android');
    expect(text).toContain('Ouvrez le menu ⋮ de Chrome');
    expect(text).toContain('Créer un raccourci');
    expect(text).toContain('ouvrira directement votre assistant technique');
  });
});
