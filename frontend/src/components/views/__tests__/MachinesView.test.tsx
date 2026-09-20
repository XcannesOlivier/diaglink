import { describe, it, expect, vi, beforeEach } from 'vitest';
import { createRoot } from 'react-dom/client';
import { act } from 'react';
import { MachinesView } from '../MachinesView';
import type { MachineDto } from '../../../types/machine';
import type { ApiResult } from '../../../types/apiResult';

const { getMachinesMock, dispatchMock } = vi.hoisted(() => ({ getMachinesMock: vi.fn(), dispatchMock: vi.fn() }));

vi.mock('../../../services/machineService', () => ({
  getMachines: getMachinesMock,
}));

vi.mock('../../../contexts/AppContext', () => ({
  useAppContext: () => ({ dispatch: dispatchMock }),
}));

function renderView(): HTMLDivElement {
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  act(() => {
    root.render(
      <MachinesView
        currentUser={{ userId: 'u1', companyId: 'c1', role: 'technician' }}
        getAccessToken={vi.fn().mockResolvedValue('token')}
      />
    );
  });
  return container;
}

async function renderAndFlush(result: ApiResult<MachineDto[]>): Promise<HTMLDivElement> {
  getMachinesMock.mockResolvedValue(result);
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  await act(async () => {
    root.render(
      <MachinesView
        currentUser={{ userId: 'u1', companyId: 'c1', role: 'technician' }}
        getAccessToken={vi.fn().mockResolvedValue('token')}
      />
    );
  });
  return container;
}

describe('MachinesView', () => {
  beforeEach(() => {
    getMachinesMock.mockReset();
    dispatchMock.mockReset();
  });

  it('shows a loading state before the request resolves', () => {
    getMachinesMock.mockReturnValue(new Promise(() => {}));
    const container = renderView();
    expect(container.textContent).toContain('Chargement des machines');
  });

  it('renders the list of accessible machines', async () => {
    const machines: MachineDto[] = [
      { id: 'm1', companyId: 'c1', name: 'Presse P1', reference: 'X100', status: 'active', hasAssistantConfigured: true, isAccessible: true },
    ];
    const container = await renderAndFlush({ kind: 'success', data: machines });

    expect(container.textContent).toContain('Presse P1');
    expect(container.textContent).toContain('Presse P1');
  });

  it('shows the empty-state message when no machine is accessible', async () => {
    const container = await renderAndFlush({ kind: 'success', data: [] });
    expect(container.textContent).toContain('Aucune machine disponible.');
  });

  it('shows an error message when the API call fails', async () => {
    const container = await renderAndFlush({ kind: 'error' });
    expect(container.textContent).toContain('Impossible de charger les données.');
  });

  it('disables "Utiliser cette machine" when the machine has no assistant configured', async () => {
    const machines: MachineDto[] = [
      { id: 'm1', companyId: 'c1', name: 'Presse P1', reference: 'X100', status: 'active', hasAssistantConfigured: false, isAccessible: true },
    ];
    const container = await renderAndFlush({ kind: 'success', data: machines });

    await act(async () => {
      container.querySelector('[role="button"]')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    const button = Array.from(container.querySelectorAll('button')).find(b => b.textContent === 'Utiliser cette machine');
    expect(button?.disabled).toBe(true);
  });

  it('dispatches MACHINE_SELECT, CHAT_CLEAR and UI_SET_VIEW when using a configured machine', async () => {
    const machines: MachineDto[] = [
      { id: 'm1', companyId: 'c1', name: 'Presse P1', reference: 'X100', status: 'active', hasAssistantConfigured: true, isAccessible: true },
    ];
    const container = await renderAndFlush({ kind: 'success', data: machines });

    await act(async () => {
      container.querySelector('[role="button"]')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    const button = Array.from(container.querySelectorAll('button')).find(b => b.textContent === 'Utiliser cette machine');
    expect(button?.disabled).toBe(false);

    await act(async () => {
      button?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(dispatchMock).toHaveBeenCalledWith(expect.objectContaining({ type: 'MACHINE_SELECT', machine: expect.objectContaining({ id: 'm1', name: 'Presse P1' }) }));
    expect(dispatchMock).toHaveBeenCalledWith({ type: 'CHAT_CLEAR' });
    expect(dispatchMock).toHaveBeenCalledWith({ type: 'UI_SET_VIEW', view: 'chat' });
  });

  it('shows an unassigned technician machine as unavailable without selecting it', async () => {
    const machines: MachineDto[] = [
      { id: 'm1', companyId: 'c1', name: 'Presse P1', reference: null, status: 'active', hasAssistantConfigured: true, isAccessible: false },
    ];
    const container = await renderAndFlush({ kind: 'success', data: machines });

    expect(container.textContent).toContain('Indisponible');
    await act(async () => {
      container.querySelector('[role="button"]')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    expect(container.textContent).not.toContain('Utiliser cette machine');
  });
});
