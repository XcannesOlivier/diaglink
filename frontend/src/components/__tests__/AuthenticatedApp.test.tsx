import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthenticatedApp } from '../AuthenticatedApp';
import { initialAppState, type AppState } from '../../types/appState';
import type { MachineDto, SelectedMachine } from '../../types/machine';

const mocks = vi.hoisted(() => ({
  state: null as unknown as AppState,
  dispatch: vi.fn(),
  getMachines: vi.fn(),
  getAccessToken: vi.fn(),
  getApiAuthHeaders: vi.fn(),
}));

vi.mock('../../hooks/useAppState', () => ({
  useAppState: () => ({ state: mocks.state, dispatch: mocks.dispatch }),
}));

vi.mock('../../hooks/useAuth', () => ({
  useAuth: () => ({ getAccessToken: mocks.getAccessToken }),
}));

vi.mock('../../services/machineService', () => ({ getMachines: mocks.getMachines }));
vi.mock('../../utils/apiAuth', () => ({ getApiAuthHeaders: mocks.getApiAuthHeaders }));
vi.mock('../layout/AppShell', () => ({
  AppShell: ({ agentId, agentName }: { agentId: string; agentName: string }) => (
    <div data-testid="app-shell" data-agent-id={agentId}>{agentName}</div>
  ),
}));

function state(role: 'technician' | 'diaglink_super_admin', selected: SelectedMachine | null = null): AppState {
  return {
    ...initialAppState,
    auth: {
      ...initialAppState.auth,
      status: 'authenticated',
      currentUser: { userId: 'user-1', companyId: 'company-1', role, email: 'user@example.test' },
    },
    machine: { selected },
  };
}

describe('AuthenticatedApp initialization', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    mocks.state = state('diaglink_super_admin');
    mocks.dispatch.mockReset();
    mocks.getMachines.mockReset();
    mocks.getAccessToken.mockReset().mockResolvedValue(null);
    mocks.getApiAuthHeaders.mockReset().mockResolvedValue({
      headers: { 'X-DiagLink-Session': 'test-session' },
      mode: 'diaglink',
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
  });

  it('renders the Super Admin shell with generic agent metadata when no machine exists', async () => {
    mocks.getMachines.mockResolvedValue({ kind: 'success', data: [] });

    await act(async () => root.render(<AuthenticatedApp />));

    const shell = container.querySelector<HTMLElement>('[data-testid="app-shell"]');
    expect(shell).not.toBeNull();
    expect(shell?.dataset.agentId).toBe('fallback-agent');
    expect(shell?.textContent).toBe('Assistant Technique');
    expect(container.textContent).not.toContain('Chargement...');
    expect(container.textContent).not.toContain('Aucune machine disponible');
  });

  it('preserves the no-machine screen for a non-Super-Admin user', async () => {
    mocks.state = state('technician');
    mocks.getMachines.mockResolvedValue({ kind: 'success', data: [] });

    await act(async () => root.render(<AuthenticatedApp />));

    expect(container.textContent).toContain('Aucune machine disponible');
    expect(container.querySelector('[data-testid="app-shell"]')).toBeNull();
  });

  it('ends initialization with an explicit error when the machines API fails', async () => {
    mocks.getMachines.mockResolvedValue({ kind: 'error' });

    await act(async () => root.render(<AuthenticatedApp />));

    expect(container.textContent).toContain('Impossible de charger la liste des machines.');
    expect(container.textContent).not.toContain('Chargement...');
  });

  it('still selects the first machine and loads its agent metadata', async () => {
    const machine: MachineDto = {
      id: 'machine-1',
      companyId: 'company-1',
      name: 'Presse P1',
      reference: 'PRS-01',
      status: 'active',
      hasAssistantConfigured: true,
      isAccessible: true,
    };
    mocks.getMachines.mockResolvedValue({ kind: 'success', data: [machine] });
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        id: 'agent-1',
        object: 'agent',
        createdAt: 1,
        name: 'Agent Presse',
        model: 'model',
        capabilities: { imageAttachments: false, fileAttachments: false },
      }),
    });
    vi.stubGlobal('fetch', fetchMock);

    await act(async () => root.render(<AuthenticatedApp />));

    expect(mocks.dispatch).toHaveBeenCalledWith({
      type: 'MACHINE_SELECT',
      machine: { id: 'machine-1', name: 'Presse P1', reference: 'PRS-01' },
    });

    mocks.state = state('diaglink_super_admin', {
      id: 'machine-1',
      name: 'Presse P1',
      reference: 'PRS-01',
    });
    await act(async () => root.render(<AuthenticatedApp />));

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/agent?machineId=machine-1',
      { headers: { 'X-DiagLink-Session': 'test-session', 'Content-Type': 'application/json' } },
    );
    expect(container.querySelector<HTMLElement>('[data-testid="app-shell"]')?.dataset.agentId).toBe('agent-1');
    expect(container.textContent).toContain('Assistant-Technique-Presse P1');
  });
});
