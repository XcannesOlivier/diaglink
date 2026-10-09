import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CompanyBrandingView } from '../CompanyBrandingView';
import type { CurrentUser } from '../../../types/currentUser';

const mocks = vi.hoisted(() => ({
  uploadLogo: vi.fn(), deleteLogo: vi.fn(), updateAccentColor: vi.fn(), resetBranding: vi.fn(),
}));
vi.mock('../../../services/companyBrandingService', async importOriginal => ({
  ...await importOriginal<typeof import('../../../services/companyBrandingService')>(),
  ...mocks,
}));

describe('CompanyBrandingView', () => {
  let root: Root;
  let container: HTMLDivElement;
  let currentUser: CurrentUser | null;
  let logoObjectUrl: string | null;
  const getAccessToken = vi.fn().mockResolvedValue('token');
  const refresh = vi.fn().mockResolvedValue(true);
  const sessionExpired = vi.fn();

  async function render() {
    await act(async () => root.render(<CompanyBrandingView currentUser={currentUser} logoObjectUrl={logoObjectUrl}
      getAccessToken={getAccessToken} onCurrentUserRefresh={refresh} onDiagLinkSessionExpired={sessionExpired} />));
  }
  function button(label: string): HTMLButtonElement {
    const result = Array.from(document.body.querySelectorAll<HTMLButtonElement>('button')).find(el => el.textContent === label);
    expect(result, `button ${label}`).toBeDefined();
    return result!;
  }
  async function click(label: string) { await act(async () => button(label).click()); }
  async function choose(value: string) {
    await act(async () => container.querySelector<HTMLInputElement>(`input[type="radio"][value="${value}"]`)!.click());
  }
  async function selectFile(file: File) {
    const input = container.querySelector<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', { configurable: true, value: [file] });
    await act(async () => input.dispatchEvent(new Event('change', { bubbles: true })));
  }
  function setExistingLogo() {
    currentUser = { ...currentUser!, companyBranding: { companyName: 'Acme', accentColor: '#39756B', hasLogo: true, logoVersion: 'v1' } };
    logoObjectUrl = 'blob:existing';
  }

  beforeEach(() => {
    for (const mock of Object.values(mocks)) mock.mockReset().mockResolvedValue({ kind: 'success', data: {} });
    refresh.mockReset().mockResolvedValue(true);
    sessionExpired.mockReset();
    vi.stubGlobal('URL', Object.assign(class extends URL {}, {
      createObjectURL: vi.fn(), revokeObjectURL: vi.fn(),
    }));
    currentUser = { userId: 'u1', companyId: 'c1', role: 'company_admin',
      companyBranding: { companyName: 'Acme', accentColor: null, hasLogo: false, logoVersion: null } };
    logoObjectUrl = null;
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });
  afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });

  it('shows the empty logo, format limits, sections and default accent', async () => {
    await render();
    expect(container.textContent).toContain('Aucun logo configuré');
    expect(container.textContent).toContain('PNG ou JPEG/JPG — 2 Mo maximum');
    expect(container.textContent).toContain('Format horizontal recommandé (environ 3:1 à 4:1). PNG avec fond transparent conseillé.');
    expect(container.textContent).toContain('Les logos carrés ou verticaux sont acceptés, mais apparaîtront plus petits dans l’en-tête.');
    expect(container.textContent).toContain('Aperçu');
    expect(container.querySelector<HTMLInputElement>('input[value="default"]')?.checked).toBe(true);
    expect(button('Enregistrer').disabled).toBe(true);
    expect(button('Importer un logo')).toBeDefined();
  });

  it('offers exactly the six named palette colors, without Ardoise', async () => {
    await render();
    const expected = [
      ['Bleu', '#356A9A'], ['Vert', '#39756B'], ['Violet', '#77639B'],
      ['Ambre', '#986B36'], ['Terracotta', '#9A5747'], ['Pétrole', '#32707A'],
    ];
    const inputs = container.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    expect(inputs).toHaveLength(7);
    for (const [label, color] of expected) {
      const input = container.querySelector<HTMLInputElement>(`input[value="${color}"]`)!;
      expect(input).not.toBeNull();
      const id = input.id;
      expect(container.querySelector(`label[for="${id}"]`)?.textContent).toBe(label);
    }
    expect(container.textContent).not.toContain('Ardoise');
    expect(container.querySelector('input[value="#687482"]')).toBeNull();
  });

  it('retains a saved former Ardoise accent as a custom current color', async () => {
    currentUser!.companyBranding!.accentColor = '#687482';
    await render();
    expect(container.querySelector<HTMLInputElement>('input[value="#687482"]')?.checked).toBe(true);
    expect(container.textContent).toContain('Couleur actuelle');
    expect(container.textContent).not.toContain('Ardoise');
  });
  it('uses only the existing global logo in the section and preview', async () => {
    setExistingLogo();
    await render();
    expect(container.querySelectorAll('img[src="blob:existing"][alt="Logo Acme"]')).toHaveLength(2);
    expect(button('Remplacer')).toBeDefined();
    expect(button('Supprimer')).toBeDefined();
    expect(container.querySelector<HTMLInputElement>('input[value="#39756B"]')?.checked).toBe(true);
  });

  it('preserves a saved custom accent outside the preset palette', async () => {
    currentUser!.companyBranding!.accentColor = '#123abc';
    await render();
    expect(container.querySelector<HTMLInputElement>('input[value="#123ABC"]')?.checked).toBe(true);
    expect(container.textContent).toContain('Couleur actuelle');
    expect(mocks.updateAccentColor).not.toHaveBeenCalled();
  });

  it('changes the preview locally and sends the selected color only on Save', async () => {
    await render();
    await choose('#356A9A');
    expect(container.textContent).toContain('Couleur sélectionnée : #356A9A');
    expect(mocks.updateAccentColor).not.toHaveBeenCalled();
    await click('Enregistrer');
    expect(mocks.updateAccentColor).toHaveBeenCalledExactlyOnceWith(getAccessToken, '#356A9A');
    expect(refresh).toHaveBeenCalledOnce();
    expect(container.querySelector('[role="status"]')?.textContent).toContain('mise à jour');
  });

  it('opens the extended palette with accessible choices and sufficient white-text contrast', async () => {
    await render();
    await click('Plus de choix de couleurs');
    const dialog = document.body.querySelector('[role="dialog"]')!;
    expect(dialog.textContent).toContain('Choisir une couleur');
    const choices = dialog.querySelectorAll<HTMLButtonElement>('button[aria-pressed]');
    expect(choices).toHaveLength(30);
    const luminance = (hex: string) => {
      const channels = [1, 3, 5].map(offset => {
        const c = Number.parseInt(hex.slice(offset, offset + 2), 16) / 255;
        return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
      });
      return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
    };
    for (const choice of choices) {
      const hex = choice.title.slice(-7);
      expect(hex).toMatch(/^#[0-9A-F]{6}$/);
      expect(choice.getAttribute('aria-label')).toBeTruthy();
      expect(1.05 / (luminance(hex) + 0.05), choice.title).toBeGreaterThanOrEqual(4.5);
    }
    expect(mocks.updateAccentColor).not.toHaveBeenCalled();
  });

  it('confirms only the local draft and saves the extra color solely on Enregistrer', async () => {
    await render();
    await click('Plus de choix de couleurs');
    await click('Bordeaux');
    expect(button('Bordeaux').getAttribute('aria-pressed')).toBe('true');
    expect(button('Bordeaux').querySelector('svg')).not.toBeNull();
    expect(container.textContent).toContain('Couleur sélectionnée : DiagLink par défaut');
    for (const mock of Object.values(mocks)) expect(mock).not.toHaveBeenCalled();
    await click('Choisir cette couleur');
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
    expect(container.textContent).toContain('Couleur sélectionnée : Bordeaux (#6B3045)');
    expect(container.querySelector('input[type="radio"]:checked')).toBeNull();
    expect(container.querySelector<HTMLElement>('[style*="border-left-color"]')?.style.borderLeftColor).toBe('rgb(107, 48, 69)');
    expect(mocks.updateAccentColor).not.toHaveBeenCalled();
    await click('Enregistrer');
    expect(mocks.updateAccentColor).toHaveBeenCalledExactlyOnceWith(getAccessToken, '#6B3045');
    expect(refresh).toHaveBeenCalledOnce();
  });

  it('cancels without changing the draft and reopens on the last confirmed selection', async () => {
    await render();
    await choose('#39756B');
    await click('Plus de choix de couleurs');
    expect(button('Vert').getAttribute('aria-pressed')).toBe('true');
    await click('Bordeaux');
    await click('Annuler');
    expect(container.querySelector<HTMLInputElement>('input[value="#39756B"]')?.checked).toBe(true);
    await click('Plus de choix de couleurs');
    expect(button('Vert').getAttribute('aria-pressed')).toBe('true');
    expect(button('Bordeaux').getAttribute('aria-pressed')).toBe('false');
    expect(mocks.updateAccentColor).not.toHaveBeenCalled();
  });

  it('closes with Escape without confirming a tentative modal selection', async () => {
    await render();
    await click('Plus de choix de couleurs');
    await click('Framboise');
    await act(async () => button('Framboise').dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })));
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
    expect(container.querySelector<HTMLInputElement>('input[value="default"]')?.checked).toBe(true);
    expect(mocks.updateAccentColor).not.toHaveBeenCalled();
  });

  it('recognizes a saved extra color without selecting a quick radio', async () => {
    currentUser!.companyBranding!.accentColor = '#6b3045';
    await render();
    expect(container.textContent).toContain('Couleur sélectionnée : Bordeaux (#6b3045)');
    expect(container.querySelectorAll('input[type="radio"]')).toHaveLength(7);
    expect(container.querySelector('input[type="radio"]:checked')).toBeNull();
    await click('Plus de choix de couleurs');
    expect(button('Bordeaux').getAttribute('aria-pressed')).toBe('true');
  });

  it('retains an unknown saved color in both the page and the modal', async () => {
    currentUser!.companyBranding!.accentColor = '#123abc';
    await render();
    await click('Plus de choix de couleurs');
    expect(button('Couleur actuelle').getAttribute('aria-pressed')).toBe('true');
    await click('Annuler');
    expect(container.querySelector<HTMLInputElement>('input[value="#123ABC"]')?.checked).toBe(true);
  });

  it('saves null for the default accent', async () => {
    setExistingLogo();
    await render();
    await choose('default');
    await click('Enregistrer');
    expect(mocks.updateAccentColor).toHaveBeenCalledWith(getAccessToken, null);
    expect(refresh).toHaveBeenCalledOnce();
  });

  it.each([false, true])('uploads a logo (replacing=%s) and refreshes without creating an Object URL', async existing => {
    if (existing) setExistingLogo();
    await render();
    const file = new File(['png'], 'logo.png', { type: 'image/png' });
    await selectFile(file);
    expect(mocks.uploadLogo).toHaveBeenCalledExactlyOnceWith(getAccessToken, file);
    expect(refresh).toHaveBeenCalledOnce();
    expect(container.querySelector<HTMLInputElement>('input[type="file"]')!.value).toBe('');
    expect(URL.createObjectURL).not.toHaveBeenCalled();
    expect(URL.revokeObjectURL).not.toHaveBeenCalled();
  });

  it('retains the unsaved accent during logo operations and metadata refresh', async () => {
    await render();
    await choose('#77639B');
    await selectFile(new File(['jpg'], 'logo.jpg', { type: 'image/jpeg' }));
    setExistingLogo();
    await render();
    expect(container.querySelector<HTMLInputElement>('input[value="#77639B"]')?.checked).toBe(true);
    expect(mocks.updateAccentColor).not.toHaveBeenCalled();
  });

  it('rejects invalid files locally without uploading or refreshing', async () => {
    await render();
    await selectFile(new File(['svg'], 'logo.svg', { type: 'image/svg+xml' }));
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('PNG ou JPEG/JPG');
    expect(mocks.uploadLogo).not.toHaveBeenCalled();
    expect(refresh).not.toHaveBeenCalled();
  });

  it('deletes the logo and refreshes metadata', async () => {
    setExistingLogo();
    await render();
    await click('Supprimer');
    expect(mocks.deleteLogo).toHaveBeenCalledExactlyOnceWith(getAccessToken);
    expect(refresh).toHaveBeenCalledOnce();
  });

  it('confirms full reset, clears an unsaved selection, and refreshes', async () => {
    setExistingLogo();
    await render();
    await choose('#986B36');
    await click('Rétablir l’apparence DiagLink');
    expect(mocks.resetBranding).not.toHaveBeenCalled();
    expect(document.body.querySelector('[role="dialog"]')?.textContent).toContain('pour tous les utilisateurs');
    await click('Rétablir');
    expect(mocks.resetBranding).toHaveBeenCalledExactlyOnceWith(getAccessToken);
    expect(refresh).toHaveBeenCalledOnce();
    expect(container.querySelector<HTMLInputElement>('input[value="default"]')?.checked).toBe(true);
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
  });

  it('does not reset on cancellation', async () => {
    await render();
    await click('Rétablir l’apparence DiagLink');
    await click('Annuler');
    expect(mocks.resetBranding).not.toHaveBeenCalled();
  });

  it.each(['save', 'upload', 'delete', 'reset'] as const)('blocks duplicate submissions and disables controls during %s', async action => {
    setExistingLogo();
    let finish!: (value: { kind: 'success'; data: object }) => void;
    const pending = new Promise<{ kind: 'success'; data: object }>(resolve => { finish = resolve; });
    const mock = action === 'save' ? mocks.updateAccentColor : action === 'upload' ? mocks.uploadLogo
      : action === 'delete' ? mocks.deleteLogo : mocks.resetBranding;
    mock.mockReturnValue(pending);
    await render();
    if (action === 'save') {
      await choose('#356A9A');
      await act(async () => { button('Enregistrer').click(); button('Enregistrer').click(); });
    }
    if (action === 'upload') await selectFile(new File(['png'], 'logo.png', { type: 'image/png' }));
    if (action === 'delete') await click('Supprimer');
    if (action === 'reset') { await click('Rétablir l’apparence DiagLink'); await click('Rétablir'); }
    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(button('Remplacer').disabled).toBe(true);
    expect(button('Enregistrer').disabled).toBe(true);
    expect(button('Supprimer').disabled).toBe(true);
    expect(container.querySelector<HTMLInputElement>('input[type="file"]')!.disabled).toBe(true);
    expect(container.querySelector<HTMLInputElement>('input[type="radio"]')!.disabled).toBe(true);
    if (action === 'save') await click('Enregistrer');
    if (action === 'upload') await selectFile(new File(['png'], 'second.png', { type: 'image/png' }));
    expect(mock).toHaveBeenCalledOnce();
    await act(async () => finish({ kind: 'success', data: {} }));
    expect(refresh).toHaveBeenCalledOnce();
  });

  it('sanitizes server errors and does not refresh on failed writes', async () => {
    mocks.updateAccentColor.mockResolvedValue({ kind: 'validation-error', message: 'Internal Blob path: private/logo' });
    await render();
    await choose('#356A9A');
    await click('Enregistrer');
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Vérifiez');
    expect(container.textContent).not.toContain('private/logo');
    expect(refresh).not.toHaveBeenCalled();
    expect(button('Enregistrer').disabled).toBe(false);
  });

  it('handles expiry using the existing logout callback', async () => {
    mocks.uploadLogo.mockResolvedValue({ kind: 'unauthorized', diagLinkSessionExpired: true });
    await render();
    await selectFile(new File(['png'], 'logo.png', { type: 'image/png' }));
    expect(sessionExpired).toHaveBeenCalledOnce();
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('session a expiré');
  });

  it('closes the reset confirmation and reports a failed reset without discarding the draft', async () => {
    mocks.resetBranding.mockResolvedValue({ kind: 'error' });
    await render();
    await choose('#986B36');
    await click('Rétablir l’apparence DiagLink');
    await click('Rétablir');
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Impossible');
    expect(container.querySelector<HTMLInputElement>('input[value="#986B36"]')?.checked).toBe(true);
    expect(refresh).not.toHaveBeenCalled();
  });

  it('reports unexpected write failure and allows retry', async () => {
    mocks.updateAccentColor.mockRejectedValue(new TypeError('private diagnostic'));
    await render();
    await choose('#356A9A');
    await click('Enregistrer');
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Impossible');
    expect(container.textContent).not.toContain('private diagnostic');
    expect(button('Enregistrer').disabled).toBe(false);
    expect(refresh).not.toHaveBeenCalled();
  });

  it('retries only the refresh after a successful write with failed auth/me', async () => {
    refresh.mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    await render();
    await selectFile(new File(['png'], 'logo.png', { type: 'image/png' }));
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('modification est enregistrée');
    expect(button('Importer un logo').disabled).toBe(true);
    await click('Actualiser les données');
    expect(mocks.uploadLogo).toHaveBeenCalledOnce();
    expect(refresh).toHaveBeenCalledTimes(2);
    expect(button('Importer un logo').disabled).toBe(false);
  });

  it('keeps the refresh-only recovery available after a rejected refresh and retry', async () => {
    refresh.mockRejectedValue(new TypeError('offline'));
    await render();
    await selectFile(new File(['png'], 'logo.png', { type: 'image/png' }));
    await click('Actualiser les données');
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Impossible d’actualiser');
    expect(button('Importer un logo').disabled).toBe(true);
    expect(button('Actualiser les données').disabled).toBe(false);
    expect(mocks.uploadLogo).toHaveBeenCalledOnce();
  });

  it('shows a loading state while identity is unresolved and denies non-admin roles', async () => {
    const user = currentUser!;
    currentUser = null;
    await render();
    expect(container.textContent).toContain('Chargement de la personnalisation');
    for (const role of ['technician', 'diaglink_super_admin'] as const) {
      currentUser = { ...user, role };
      await render();
      expect(container.textContent).toContain('Accès non autorisé');
      expect(container.querySelector('input')).toBeNull();
    }
  });
});
