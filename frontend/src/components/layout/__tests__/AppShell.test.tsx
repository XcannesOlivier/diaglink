import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import { act } from 'react';
import { AppShell } from '../AppShell';
import { initialAppState } from '../../../types/appState';
import type { AppState } from '../../../types/appState';
import type { CurrentUser } from '../../../types/currentUser';
import { ROLE_LABELS } from '../../../utils/navigation';
import { clearDiagLinkSession, setDiagLinkSession } from '../../../utils/apiAuth';
import { ThemeProvider } from '../../ThemeProvider';
import { darkTheme, lightTheme } from '../../../config/themes';

let mockState: AppState;
const listMachineRequestsMock = vi.hoisted(() => vi.fn());
const machineRequestsViewState = vi.hoisted(() => ({ resetKeys: [] as number[] }));
const fetchCompanyLogoMock = vi.hoisted(() => vi.fn());
const mounted: { root: Root; container: HTMLDivElement }[] = [];
afterEach(async () => {
  for (const { root, container } of mounted.splice(0)) { await act(async () => root.unmount()); container.remove(); }
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  clearDiagLinkSession();
});
const mockDispatch = vi.fn();

// AgentChat -> useAuth -> useMsal needs an MSAL context; stub it so mounting doesn't require MsalProvider.
const msal = vi.hoisted(() => ({ instance: {}, accounts: [] as never[] }));
vi.mock('@azure/msal-react', () => ({ useMsal: () => msal }));
vi.mock('../../../services/companyBrandingService', async importOriginal => ({
  ...await importOriginal<typeof import('../../../services/companyBrandingService')>(),
  fetchCompanyLogo: fetchCompanyLogoMock,
}));
vi.mock('../../../services/machineRequestAdminApi', async importOriginal => {
  const original = await importOriginal<typeof import('../../../services/machineRequestAdminApi')>();
  return { ...original, listMachineRequests: listMachineRequestsMock };
});
vi.mock('../../views/MachineRequestsView', () => ({
  MachineRequestsView: ({ listResetKey = 0 }: { listResetKey?: number }) => {
    machineRequestsViewState.resetKeys.push(listResetKey);
    return <div data-testid="machine-requests-view" data-reset-key={listResetKey}>Liste des demandes</div>;
  },
}));
vi.mock('../../core/SupportContactDialog', () => ({
  SupportContactDialog: ({ open, machineId }: { open: boolean; machineId?: string }) =>
    open ? <div role="dialog" data-machine-id={machineId}>Modale support</div> : null,
}));

// AppShell/AgentChat read global state via useAppContext (used directly and via useAppState) — stub it
// with a controllable state instead of wiring up the real AppProvider (which also depends on MSAL).
vi.mock('../../../contexts/AppContext', () => ({
  useAppContext: () => ({ state: mockState, dispatch: mockDispatch }),
}));

function buildState(currentUser: CurrentUser | null, currentView: AppState['ui']['currentView'] = 'chat'): AppState {
  return {
    ...initialAppState,
    auth: { ...initialAppState.auth, status: 'authenticated', currentUser },
    branding: { ...initialAppState.branding },
    ui: { ...initialAppState.ui, currentView },
  };
}

function renderAppShell(withThemeProvider = false): HTMLDivElement {
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted.push({ root, container });
  act(() => {
    const shell = <AppShell agentId="agent-1" agentName="Assistant Technique" />;
    root.render(withThemeProvider ? <ThemeProvider>{shell}</ThemeProvider> : shell);
  });
  return container;
}

function buildBrandedState(role: CurrentUser['role'], companyName: string | null = 'Acme'): AppState {
  return {
    ...buildState({
      userId: 'u1',
      companyId: 'c1',
      role,
      firstName: 'Alice',
      lastName: 'Martin',
      companyBranding: { companyName, accentColor: null, hasLogo: true, logoVersion: 'v1' },
    }),
    branding: { companyId: 'c1', logoVersion: 'v1', logoObjectUrl: 'blob:company-v1' },
  };
}

describe('AppShell navigation', () => {
  beforeEach(() => {
    mockDispatch.mockClear();
    listMachineRequestsMock.mockReset();
    listMachineRequestsMock.mockResolvedValue({ kind: 'success', data: [] });
    machineRequestsViewState.resetKeys.length = 0;
    fetchCompanyLogoMock.mockReset().mockResolvedValue({ logo: null, diagLinkSessionExpired: false });
    vi.stubGlobal('URL', Object.assign(class extends URL {}, {
      createObjectURL: vi.fn(),
      revokeObjectURL: vi.fn(),
    }));
    window.history.replaceState(null, '', '/app');
  });

  it.each([
    ['Light', '#9A5747', 'company_admin'],
    ['Dark', '#9A5747', 'company_admin'],
    ['Light', null, 'company_admin'],
    ['Dark', null, 'company_admin'],
    ['Light', null, 'diaglink_super_admin'],
    ['Dark', null, 'diaglink_super_admin'],
  ] as const)('opens theme choices without remounting or changing identity/theme (%s, %s, %s)', async (theme, accent, role) => {
    const previousTheme = localStorage.getItem('ai-foundry-theme');
    localStorage.setItem('ai-foundry-theme', theme);
    try {
      mockState = buildState({ userId: 'u1', companyId: 'c1', role,
        companyBranding: role === 'diaglink_super_admin' ? null : {
          companyName: 'Acme', accentColor: accent, hasLogo: false, logoVersion: null,
        } });
      const container = renderAppShell(true);
      const providers = Array.from(container.querySelectorAll('.fui-FluentProvider'));
      const shell = container.querySelector('header')!;
      const openSettings = container.querySelector<HTMLButtonElement>('button[aria-label="Paramètres"]')!;
      await act(async () => openSettings.click());
      const drawer = document.body.querySelector('[role="dialog"]')!;
      const dropdown = drawer.querySelector<HTMLButtonElement>('[role="combobox"]')!;
      const requestsBefore = mockDispatch.mock.calls.length;
      const fetchSpy = vi.spyOn(globalThis, 'fetch');
      fetchSpy.mockClear();
      await act(async () => dropdown.querySelector<HTMLElement>('.fui-Dropdown__expandIcon')!.click());
      const listbox = document.body.querySelector('[role="listbox"]')!;
      expect(dropdown.getAttribute('aria-expanded')).toBe('true');
      expect(document.body.querySelector('[role="dialog"]')).toBe(drawer);
      expect(container.querySelector('header')).toBe(shell);
      expect(Array.from(container.querySelectorAll('.fui-FluentProvider'))).toEqual(providers);
      expect(localStorage.getItem('ai-foundry-theme')).toBe(theme);
      expect(mockDispatch.mock.calls).toHaveLength(requestsBefore);
      expect(fetchSpy).not.toHaveBeenCalled();
      const listboxPortal = listbox.closest('[data-portal-node]')!;
      expect(listboxPortal.classList.contains('fui-FluentProvider')).toBe(false);
      const drawerPortal = drawer.closest('[data-portal-node]')!;
      expect(drawerPortal.classList.contains('fui-FluentProvider')).toBe(false);
      expect(getComputedStyle(drawerPortal).getPropertyValue('--colorBrandBackground').trim())
        .toBe(accent ?? (theme === 'Dark' ? darkTheme : lightTheme).colorBrandBackground);
      for (const [label, saved, base] of [['Clair', 'Light', lightTheme], ['Sombre', 'Dark', darkTheme]] as const) {
        if (dropdown.getAttribute('aria-expanded') !== 'true') await act(async () => dropdown.click());
        const option = Array.from(document.body.querySelectorAll<HTMLElement>('[role="option"]'))
          .find(el => el.textContent === label)!;
        await act(async () => option.click());
        expect(localStorage.getItem('ai-foundry-theme')).toBe(saved);
        expect(document.body.querySelector('[role="dialog"]')).toBe(drawer);
        expect(container.querySelector('header')).toBe(shell);
        expect(Array.from(container.querySelectorAll('.fui-FluentProvider'))).toEqual(providers);
        await act(async () => dropdown.click());
        const selectedPortal = document.body.querySelector('[role="listbox"]')!.closest('[data-portal-node]')!;
        expect(getComputedStyle(selectedPortal).getPropertyValue('--colorBrandBackground').trim())
          .toBe(accent ?? base.colorBrandBackground);
        await act(async () => dropdown.click());
      }
      expect(fetchSpy).not.toHaveBeenCalled();
    } finally {
      if (previousTheme === null) localStorage.removeItem('ai-foundry-theme');
      else localStorage.setItem('ai-foundry-theme', previousTheme);
    }
  });

  it('opens the administration URL for a Super Admin', () => {
    window.history.replaceState(null, '', '/app/administration');
    mockState = buildState({ userId: 'u3', companyId: 'c1', role: 'diaglink_super_admin' });
    renderAppShell();
    expect(mockDispatch).toHaveBeenCalledWith({ type: 'UI_SET_VIEW', view: 'diaglink-admin' });
    expect(window.location.pathname).toBe('/app/administration');
  });

  it('preserves the URL until login and role loading complete', () => {
    window.history.replaceState(null, '', '/app/administration');
    mockState = buildState(null);
    renderAppShell();
    expect(mockDispatch).not.toHaveBeenCalledWith({ type: 'UI_SET_VIEW', view: 'diaglink-admin' });
    expect(window.location.pathname).toBe('/app/administration');
    mockState = buildState({ userId: 'u3', companyId: 'c1', role: 'diaglink_super_admin' });
    act(() => mounted[0].root.render(<AppShell agentId="agent-1" agentName="Assistant Technique" />));
    expect(mockDispatch).toHaveBeenCalledWith({ type: 'UI_SET_VIEW', view: 'diaglink-admin' });
  });

  it.each(['technician', 'company_admin'] as const)('rejects direct administration access for %s', role => {
    window.history.replaceState(null, '', '/app/administration');
    mockState = buildState({ userId: 'u1', companyId: 'c1', role });
    renderAppShell();
    expect(mockDispatch).toHaveBeenCalledWith({ type: 'UI_SET_VIEW', view: 'chat' });
    expect(window.location.pathname).toBe('/app');
  });

  it('updates the URL from navigation and handles browser history', () => {
    mockState = buildState({ userId: 'u3', companyId: 'c1', role: 'diaglink_super_admin' });
    const container = renderAppShell();
    const tab = Array.from(container.querySelectorAll('button')).find(el => el.textContent?.includes('Administration DiagLink'));
    expect(tab).toBeDefined();
    act(() => (tab as HTMLElement).click());
    expect(window.location.pathname).toBe('/app/administration');
    expect(mockDispatch).toHaveBeenCalledWith({ type: 'UI_SET_VIEW', view: 'diaglink-admin' });
    act(() => {
      window.history.replaceState({ diaglinkView: 'machines' }, '', '/app');
      window.dispatchEvent(new PopStateEvent('popstate'));
    });
    expect(mockDispatch).toHaveBeenLastCalledWith({ type: 'UI_SET_VIEW', view: 'machines' });
  });

  it('returns the active requests view to its list when its desktop tab is selected again', () => {
    mockState = buildState({ userId: 'u3', companyId: 'c1', role: 'diaglink_super_admin' }, 'machine-requests');
    const container = renderAppShell();
    const tab = Array.from(container.querySelectorAll('button')).find(button => button.textContent?.includes('Nouvelles demandes'))!;

    expect(container.querySelector('[data-testid="machine-requests-view"]')?.getAttribute('data-reset-key')).toBe('0');
    act(() => tab.click());

    expect(container.querySelector('[data-testid="machine-requests-view"]')?.getAttribute('data-reset-key')).toBe('1');
    expect(mockDispatch).not.toHaveBeenCalledWith({ type: 'UI_SET_VIEW', view: 'machine-requests' });
  });

  it('does not reset request details from the mobile and tablet navigation', () => {
    mockState = buildState({ userId: 'u3', companyId: 'c1', role: 'diaglink_super_admin' }, 'chat');
    const container = renderAppShell();
    const menuButton = container.querySelector<HTMLButtonElement>('button[aria-label="Ouvrir le menu"]')!;
    act(() => menuButton.click());
    const requestButtons = Array.from(document.body.querySelectorAll<HTMLButtonElement>('button'))
      .filter(button => button.textContent?.includes('Nouvelles demandes'));

    act(() => requestButtons.at(-1)!.click());

    expect(machineRequestsViewState.resetKeys).toHaveLength(0);
    expect(mockDispatch).toHaveBeenCalledWith({ type: 'UI_SET_VIEW', view: 'machine-requests' });
  });

  it('spaces and vertically aligns the Super Admin mobile requests badge', async () => {
    listMachineRequestsMock.mockResolvedValue({
      kind: 'success',
      data: Array.from({ length: 12 }, (_, index) => ({ requestId: `request-${index}`, status: 'pending' })),
    });
    mockState = buildState({ userId: 'u3', companyId: 'c1', role: 'diaglink_super_admin' });
    const container = renderAppShell();
    await act(async () => { await Promise.resolve(); });
    act(() => container.querySelector<HTMLButtonElement>('button[aria-label="Ouvrir le menu"]')!.click());

    const label = document.body.querySelector<HTMLElement>('[data-mobile-request-label]')!;
    expect(label.textContent).toBe('Nouvelles demandes12');
    expect(label.querySelector('[class*="fui-Badge"]')?.textContent).toBe('12');
    expect(getComputedStyle(label).display).toBe('inline-flex');
    expect(getComputedStyle(label).alignItems).toBe('center');
    expect(getComputedStyle(label).gap).toBe('8px');
  });

  it('technician does not see Utilisateurs / Entreprise / Administration DiagLink', () => {
    mockState = buildState({ userId: 'u1', companyId: 'c1', role: 'technician' });
    const container = renderAppShell();
    const text = container.textContent ?? '';

    expect(text).toContain('Machines');
    expect(text).not.toContain('Historique');
    expect(text).not.toContain('Utilisateurs');
    expect(text).not.toContain('Entreprise');
    expect(text).not.toContain('Administration DiagLink');
    expect(text).not.toContain('Nouvelles demandes');
  });

  it('company_admin sees Utilisateurs + Entreprise but not Administration DiagLink', () => {
    mockState = buildState({ userId: 'u2', companyId: 'c1', role: 'company_admin' });
    const container = renderAppShell();
    const text = container.textContent ?? '';

    expect(text).toContain('Utilisateurs');
    expect(text).toContain('Crédits et abonnement');
    expect(text).not.toContain('Personnalisation');
    expect(text).not.toContain('Historique');
    expect(text).not.toContain('Entreprises');
    expect(text).not.toContain('Administration DiagLink');
    expect(text).not.toContain('Nouvelles demandes');
  });

  it.each(['technician', 'diaglink_super_admin'] as const)('does not offer personalization for %s on desktop or in the drawer', role => {
    mockState = buildState({ userId: 'u1', companyId: 'c1', role });
    const container = renderAppShell();
    expect(container.querySelector('header button[aria-label="Personnalisation"]')).toBeNull();
    act(() => container.querySelector<HTMLButtonElement>('button[aria-label="Ouvrir le menu"]')!.click());
    expect(document.body.querySelector('[role="dialog"]')?.textContent).not.toContain('Personnalisation');
  });

  it('navigates to the existing personalization view from settings, not the main drawer', () => {
    mockState = buildState({ userId: 'u1', companyId: 'c1', role: 'company_admin' });
    const container = renderAppShell();
    expect(container.querySelector('header button[aria-label="Personnalisation"]')).toBeNull();
    act(() => container.querySelector<HTMLButtonElement>('button[aria-label="Ouvrir le menu"]')!.click());
    expect(document.body.querySelector('[role="dialog"]')?.textContent).not.toContain('Personnalisation');
    const settings = Array.from(document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] button'))
      .find(button => button.textContent === 'Paramètres')!;
    act(() => settings.click());
    const entry = Array.from(document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] button'))
      .find(button => button.textContent === 'Personnalisation')!;
    act(() => entry.click());
    expect(mockDispatch).toHaveBeenLastCalledWith({ type: 'UI_SET_VIEW', view: 'personalization' });
    expect(window.location.pathname).toBe('/app');
  });

  it.each(['save', 'upload', 'delete', 'reset'] as const)('refreshes /api/auth/me and dispatches metadata after personalization %s', async action => {
    setDiagLinkSession('test-session', '2099-01-01T00:00:00Z');
    mockState = { ...buildBrandedState('company_admin'), ui: { ...initialAppState.ui, currentView: 'personalization' } };
    const updatedUser: CurrentUser = { ...mockState.auth.currentUser!,
      companyBranding: { companyName: 'Acme', accentColor: action === 'save' ? '#356A9A' : null,
        hasLogo: action === 'upload', logoVersion: action === 'upload' ? 'v2' : null } };
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ success: true }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(updatedUser), { status: 200 }));
    const container = renderAppShell();
    const clickAction = (label: string) => {
      const button = Array.from(document.body.querySelectorAll<HTMLButtonElement>('button')).find(el => el.textContent === label)!;
      button.click();
    };
    await act(async () => {
      if (action === 'save') {
        container.querySelector<HTMLInputElement>('input[value="#356A9A"]')!.click();
      } else if (action === 'upload') {
        const input = container.querySelector<HTMLInputElement>('input[type="file"]')!;
        Object.defineProperty(input, 'files', { configurable: true, value: [new File(['png'], 'logo.png', { type: 'image/png' })] });
        input.dispatchEvent(new Event('change', { bubbles: true }));
      } else clickAction(action === 'delete' ? 'Supprimer' : 'Rétablir l’apparence DiagLink');
    });
    if (action === 'save' || action === 'reset') await act(async () => clickAction(action === 'save' ? 'Enregistrer' : 'Rétablir'));
    expect(fetchMock).toHaveBeenNthCalledWith(1,
      action === 'upload' || action === 'delete' ? '/api/company/branding/logo' : '/api/company/branding',
      expect.objectContaining({ method: action === 'save' ? 'PATCH' : action === 'upload' ? 'PUT' : 'DELETE' }));
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/auth/me', { headers: { 'X-DiagLink-Session': 'test-session' } });
    expect(mockDispatch).toHaveBeenCalledWith({ type: 'AUTH_CURRENT_USER_LOADED', currentUser: updatedUser });
  });

  it.each(['technician', 'company_admin'] as const)('shows support contact actions for %s', role => {
    mockState = buildState({ userId: 'u1', companyId: 'c1', role });
    const container = renderAppShell();
    const contactButtons = Array.from(document.body.querySelectorAll('button'))
      .filter(button => button.textContent?.trim() === 'Contacter DiagLink');

    expect(contactButtons.length).toBeGreaterThanOrEqual(1);
    expect(container.textContent).not.toContain('Modale support');
  });

  it('places the support action before settings in the mobile drawer', () => {
    mockState = buildState({ userId: 'u1', companyId: 'c1', role: 'technician' });
    const container = renderAppShell();
    const menuButton = container.querySelector('button[aria-label="Ouvrir le menu"]');

    expect(menuButton).not.toBeNull();
    act(() => (menuButton as HTMLButtonElement).click());

    const actionLabels = Array.from(document.body.querySelectorAll('button'))
      .map(button => button.textContent?.trim());
    const contactIndexes = actionLabels.flatMap((label, index) => label === 'Contacter DiagLink' ? [index] : []);
    const settingsIndexes = actionLabels.flatMap((label, index) => label === 'Paramètres' ? [index] : []);
    expect(contactIndexes).toHaveLength(2);
    expect(settingsIndexes).toHaveLength(2);
    expect(contactIndexes[1]).toBeLessThan(settingsIndexes[1]);
  });

  it('opens support contact without navigation and passes the selected machine', () => {
    mockState = {
      ...buildState({ userId: 'u1', companyId: 'c1', role: 'technician' }),
      machine: { selected: { id: 'machine-42', name: 'Presse 4', reference: 'PR-004' } },
    };
    const container = renderAppShell();
    const initialPath = window.location.pathname;
    const contactButton = Array.from(container.querySelectorAll('button'))
      .find(button => button.textContent?.trim() === 'Contacter DiagLink');

    expect(contactButton).toBeDefined();
    act(() => contactButton!.click());

    expect(window.location.pathname).toBe(initialPath);
    expect(container.querySelector('[role="dialog"]')?.getAttribute('data-machine-id')).toBe('machine-42');
  });

  it('diaglink_super_admin sees requests and the existing administration entries', () => {
    mockState = buildState({ userId: 'u3', companyId: 'c1', role: 'diaglink_super_admin' });
    const container = renderAppShell();
    const text = container.textContent ?? '';

    expect(text).toContain('Entreprises');
    expect(text).toContain('Utilisateurs');
    expect(text).toContain('Machines');
    expect(text).toContain('Administration DiagLink');
    expect(text).toContain('Nouvelles demandes');
    expect(text).not.toContain('Contacter DiagLink');
  });

  it('renders the existing chat surface for the chat view', () => {
    mockState = buildState({ userId: 'u1', companyId: 'c1', role: 'technician' }, 'chat');
    const container = renderAppShell();

    // ChatInterface's starter/empty state renders the agent name passed to AgentChat.
    expect(container.textContent).toContain('Assistant Technique');
  });

  it('applies the configured company accent to the authenticated root', () => {
    mockState = buildState({
      userId: 'u1',
      companyId: 'c1',
      role: 'technician',
      companyBranding: { companyName: 'Acme', accentColor: '#12AB34', hasLogo: false, logoVersion: null },
    });

    const container = renderAppShell();

    expect((container.firstElementChild as HTMLElement).style.getPropertyValue('--diaglink-company-accent')).toBe('#12AB34');
    expect(container.firstElementChild?.hasAttribute('data-company-accent')).toBe(true);
  });

  it('falls back to the current DiagLink accent token', () => {
    mockState = buildState({
      userId: 'u1',
      companyId: 'c1',
      role: 'technician',
      companyBranding: { companyName: 'Acme', accentColor: null, hasLogo: false, logoVersion: null },
    });

    const container = renderAppShell();

    expect((container.firstElementChild as HTMLElement).style.getPropertyValue('--diaglink-company-accent')).toBe('var(--colorBrandForeground1)');
    expect(container.firstElementChild?.hasAttribute('data-company-accent')).toBe(false);
  });

  it.each(['company_admin', 'technician'] as const)('places the cached logo after the desktop role for %s', role => {
    mockState = buildBrandedState(role);
    const container = renderAppShell();
    const logo = container.querySelector<HTMLImageElement>('header img[alt="Logo Acme"]')!;

    expect(logo).not.toBeNull();
    expect(logo.getAttribute('src')).toBe('blob:company-v1');
    expect(logo.previousElementSibling?.textContent).toBe(ROLE_LABELS[role]);
    expect(logo.parentElement?.textContent).toBe(`Alice Martin${ROLE_LABELS[role]}`);
    expect(getComputedStyle(logo).maxHeight).toBe('28px');
    expect(getComputedStyle(logo).maxWidth).toBe('96px');
    expect(getComputedStyle(logo).objectFit).toBe('contain');
    expect(getComputedStyle(logo).flexShrink).toBe('0');
    expect(container.querySelector('img[alt="DiagLink"]')).not.toBeNull();
    expect(fetchCompanyLogoMock).not.toHaveBeenCalled();
    expect(URL.createObjectURL).not.toHaveBeenCalled();
    expect(container.querySelector('header button[aria-label="Contacter DiagLink"]')).not.toBeNull();
    expect(container.querySelector('header button[aria-label="Paramètres"]')).not.toBeNull();

    const machines = Array.from(container.querySelectorAll<HTMLButtonElement>('header button')).find(button => button.textContent?.includes('Machines'))!;
    act(() => machines.click());
    expect(mockDispatch).toHaveBeenLastCalledWith({ type: 'UI_SET_VIEW', view: 'machines' });
    expect(window.location.pathname).toBe('/app');
  });

  it.each(['company_admin', 'technician'] as const)('places the logo beside the existing drawer identity for %s', role => {
    mockState = buildBrandedState(role);
    const container = renderAppShell();
    act(() => container.querySelector<HTMLButtonElement>('button[aria-label="Ouvrir le menu"]')!.click());
    const drawer = document.body.querySelector('[role="dialog"]')!;
    const logo = drawer.querySelector<HTMLImageElement>('img[alt="Logo Acme"]')!;

    expect(logo).not.toBeNull();
    expect(logo.previousElementSibling?.textContent).toBe(`Alice Martin${ROLE_LABELS[role]}`);
    expect(getComputedStyle(logo.parentElement!).display).toBe('flex');
    expect(getComputedStyle(logo).maxHeight).toBe('32px');
    expect(getComputedStyle(logo).maxWidth).toBe('80px');
    expect(drawer.querySelector('img[alt="DiagLink"]')).not.toBeNull();
    expect(fetchCompanyLogoMock).not.toHaveBeenCalled();
    expect(URL.createObjectURL).not.toHaveBeenCalled();

    const machines = Array.from(drawer.querySelectorAll('button')).find(button => button.textContent === 'Machines')!;
    act(() => machines.click());
    expect(mockDispatch).toHaveBeenLastCalledWith({ type: 'UI_SET_VIEW', view: 'machines' });
    expect(window.location.pathname).toBe('/app');
  });

  it.each(['company_admin', 'technician'] as const)('leaves no logo placeholder for %s without a logo', role => {
    mockState = buildState({
      userId: 'u1', companyId: 'c1', role, firstName: 'Alice', lastName: 'Martin',
      companyBranding: { companyName: 'Acme', accentColor: null, hasLogo: false, logoVersion: null },
    });
    const container = renderAppShell();
    const roleBadge = Array.from(container.querySelectorAll('header [class*="fui-Badge"]'))
      .find(badge => badge.textContent === ROLE_LABELS[role])!;
    expect(roleBadge.parentElement?.children).toHaveLength(2);
    act(() => container.querySelector<HTMLButtonElement>('button[aria-label="Ouvrir le menu"]')!.click());
    const drawer = document.body.querySelector('[role="dialog"]')!;
    const name = Array.from(drawer.querySelectorAll('span')).find(span => span.textContent === 'Alice Martin')!;
    expect(name.parentElement?.parentElement?.children).toHaveLength(1);
    expect(document.body.querySelector('img[alt^="Logo "]')).toBeNull();
    expect(drawer.querySelector('img[alt="DiagLink"]')).not.toBeNull();
  });

  it('does not show company images for a super-admin without branding, even with a stale URL', () => {
    mockState = {
      ...buildState({ userId: 'sa', companyId: 'root', role: 'diaglink_super_admin', companyBranding: null }),
      branding: { companyId: 'c1', logoVersion: 'v1', logoObjectUrl: 'blob:stale' },
    };
    const container = renderAppShell();
    act(() => container.querySelector<HTMLButtonElement>('button[aria-label="Ouvrir le menu"]')!.click());

    expect(document.body.querySelector('img[alt^="Logo "]')).toBeNull();
    expect(document.body.querySelector('[role="dialog"] img[alt="DiagLink"]')).not.toBeNull();
  });

  it('leaves no placeholder while the Object URL is absent', () => {
    mockState = { ...buildBrandedState('technician'), branding: { ...initialAppState.branding } };
    const container = renderAppShell();
    const badge = container.querySelector('header [class*="fui-Badge"]')!;

    expect(badge.parentElement?.children).toHaveLength(2);
    expect(container.querySelector('header img')).toBeNull();
  });

  it('uses the generic alt when the company name is absent', () => {
    mockState = buildBrandedState('technician', null);
    const container = renderAppShell();

    expect(container.querySelector('header img')?.getAttribute('alt')).toBe('Logo de l’entreprise');
    act(() => container.querySelector<HTMLButtonElement>('button[aria-label="Ouvrir le menu"]')!.click());
    expect(document.body.querySelector('[role="dialog"] img[alt="Logo de l’entreprise"]')).not.toBeNull();
  });

  it('removes a broken desktop and drawer image without placeholders and accepts a new URL', () => {
    mockState = buildBrandedState('company_admin');
    const container = renderAppShell();
    act(() => container.querySelector<HTMLButtonElement>('button[aria-label="Ouvrir le menu"]')!.click());
    const logos = document.body.querySelectorAll<HTMLImageElement>('img[alt="Logo Acme"]');
    expect(logos).toHaveLength(2);

    act(() => logos.forEach(logo => logo.dispatchEvent(new Event('error'))));
    expect(document.body.querySelector('img[alt="Logo Acme"]')).toBeNull();
    expect(container.querySelector('header [class*="fui-Badge"]')?.parentElement?.children).toHaveLength(2);
    expect(document.body.querySelector('img[alt="DiagLink"]')).not.toBeNull();

    mockState = { ...mockState, branding: { ...mockState.branding, logoObjectUrl: 'blob:replacement' } };
    act(() => mounted[0].root.render(<AppShell agentId="agent-1" agentName="Assistant Technique" />));
    expect(document.body.querySelectorAll('img[src="blob:replacement"]')).toHaveLength(2);
    expect(fetchCompanyLogoMock).not.toHaveBeenCalled();
  });
});
