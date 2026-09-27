import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import { act } from 'react';
import { AppShell } from '../AppShell';
import { initialAppState } from '../../../types/appState';
import type { AppState } from '../../../types/appState';
import type { CurrentUser } from '../../../types/currentUser';

let mockState: AppState;
const listMachineRequestsMock = vi.hoisted(() => vi.fn());
const mounted: { root: Root; container: HTMLDivElement }[] = [];
afterEach(async () => {
  for (const { root, container } of mounted.splice(0)) { await act(async () => root.unmount()); container.remove(); }
});
const mockDispatch = vi.fn();

// AgentChat -> useAuth -> useMsal needs an MSAL context; stub it so mounting doesn't require MsalProvider.
const msal = vi.hoisted(() => ({ instance: {}, accounts: [] as never[] }));
vi.mock('@azure/msal-react', () => ({ useMsal: () => msal }));
vi.mock('../../../services/machineRequestAdminApi', async importOriginal => {
  const original = await importOriginal<typeof import('../../../services/machineRequestAdminApi')>();
  return { ...original, listMachineRequests: listMachineRequestsMock };
});
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
    ui: { ...initialAppState.ui, currentView },
  };
}

function renderAppShell(): HTMLDivElement {
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted.push({ root, container });
  act(() => {
    root.render(<AppShell agentId="agent-1" agentName="Assistant Technique" />);
  });
  return container;
}

describe('AppShell navigation', () => {
  beforeEach(() => {
    mockDispatch.mockClear();
    listMachineRequestsMock.mockReset();
    listMachineRequestsMock.mockResolvedValue({ kind: 'success', data: [] });
    window.history.replaceState(null, '', '/app');
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
    expect(text).not.toContain('Historique');
    expect(text).not.toContain('Entreprises');
    expect(text).not.toContain('Administration DiagLink');
    expect(text).not.toContain('Nouvelles demandes');
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
});
