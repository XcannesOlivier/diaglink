import { describe, it, expect, vi, beforeEach } from 'vitest';
import { createRoot } from 'react-dom/client';
import { act } from 'react';
import { MachinesView } from '../MachinesView';
import type { MachineDto } from '../../../types/machine';
import type { ApiResult } from '../../../types/apiResult';

const { getMachinesMock, getCompaniesMock, dispatchMock, inspectPdfFilesMock } = vi.hoisted(() => ({ getMachinesMock: vi.fn(), getCompaniesMock: vi.fn(), dispatchMock: vi.fn(), inspectPdfFilesMock: vi.fn() }));

vi.mock('../../../services/machineService', () => ({
  getMachines: getMachinesMock,
  addMachineWithDocuments: vi.fn(),
}));

vi.mock('../../../services/companyService', () => ({ getCompanies: getCompaniesMock }));

vi.mock('../../../contexts/AppContext', () => ({
  useAppContext: () => ({ dispatch: dispatchMock }),
}));

vi.mock('../../../pages/start/pdfSelection', () => ({
  formatFileSize: () => '1 Ko',
  inspectPdfFiles: inspectPdfFilesMock,
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
    document.body.innerHTML = '';
    getMachinesMock.mockReset();
    getCompaniesMock.mockReset();
    dispatchMock.mockReset();
    inspectPdfFilesMock.mockReset();
  });

  it('shows a loading state before the request resolves', () => {
    getMachinesMock.mockReturnValue(new Promise(() => {}));
    const container = renderView();
    expect(container.textContent).toContain('Chargement des machines');
  });

  it('shows the request action to a company admin but not to a technician', async () => {
    getMachinesMock.mockResolvedValue({ kind: 'success', data: [] });
    const companyContainer = document.createElement('div'); document.body.appendChild(companyContainer);
    const companyRoot = createRoot(companyContainer);
    await act(async () => companyRoot.render(<MachinesView currentUser={{ userId: 'u1', companyId: 'c1', role: 'company_admin' }} getAccessToken={async () => 'token'} />));
    expect(companyContainer.textContent).toContain('Demander l’ajout d’une machine');

    const technicianContainer = await renderAndFlush({ kind: 'success', data: [] });
    expect(technicianContainer.textContent).not.toContain('Demander l’ajout d’une machine');
  });

  it('keeps the historical direct add action for a super admin', async () => {
    getMachinesMock.mockResolvedValue({ kind: 'success', data: [] });
    getCompaniesMock.mockResolvedValue({ kind: 'success', data: [{ id: 'c1', name: 'Entreprise A', status: 'active' }] });
    const container = document.createElement('div'); document.body.appendChild(container);
    const root = createRoot(container);
    await act(async () => root.render(<MachinesView currentUser={{ userId: 'u1', companyId: 'c0', role: 'diaglink_super_admin' }} getAccessToken={async () => 'token'} />));
    const company = Array.from(container.querySelectorAll('[role="button"]')).find(item => item.textContent?.includes('Entreprise A'))!;
    await act(async () => company.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(container.textContent).toContain('Ajouter une machine');
    expect(container.textContent).not.toContain('Demander l’ajout d’une machine');
  });

  it('shows the document request only for a company admin and preserves the Super Admin PDF action', async () => {
    const machines: MachineDto[] = [{ id: 'm1', companyId: 'c1', name: 'Presse P1', reference: null, status: 'active', hasAssistantConfigured: true, isAccessible: true }];
    getMachinesMock.mockResolvedValue({ kind: 'success', data: machines });
    const companyContainer = document.createElement('div'); document.body.appendChild(companyContainer); const companyRoot = createRoot(companyContainer);
    await act(async () => companyRoot.render(<MachinesView currentUser={{ userId: 'u1', companyId: 'c1', role: 'company_admin' }} getAccessToken={async () => 'token'} />));
    await act(async () => companyContainer.querySelector('[role="button"]')?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(companyContainer.textContent).toContain('Demander l’ajout de documents');
    expect(companyContainer.textContent).not.toContain('Ajouter des PDF');

    const technicianContainer = await renderAndFlush({ kind: 'success', data: machines });
    await act(async () => technicianContainer.querySelector('[role="button"]')?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(technicianContainer.textContent).not.toContain('Demander l’ajout de documents');

    getCompaniesMock.mockResolvedValue({ kind: 'success', data: [{ id: 'c1', name: 'Entreprise A', status: 'active' }] });
    const adminContainer = document.createElement('div'); document.body.appendChild(adminContainer); const adminRoot = createRoot(adminContainer);
    await act(async () => adminRoot.render(<MachinesView currentUser={{ userId: 'sa', companyId: 'root', role: 'diaglink_super_admin' }} getAccessToken={async () => 'token'} />));
    const company = [...adminContainer.querySelectorAll('[role="button"]')].find(item => item.textContent?.includes('Entreprise A'))!;
    await act(async () => company.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    const machine = [...adminContainer.querySelectorAll('[role="button"]')].find(item => item.textContent?.includes('Presse P1'))!;
    await act(async () => machine.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(adminContainer.textContent).toContain('Ajouter des PDF');
    expect(adminContainer.textContent).not.toContain('Demander l’ajout de documents');
  });

  it('keeps the document request dialog open while clicking and selecting through its file picker', async () => {
    const machines: MachineDto[] = [{ id: 'm1', companyId: 'c1', name: 'Presse P1', reference: null, status: 'active', hasAssistantConfigured: true, isAccessible: true }];
    getMachinesMock.mockResolvedValue({ kind: 'success', data: machines });
    const container = document.createElement('div'); document.body.appendChild(container); const root = createRoot(container);
    await act(async () => root.render(<MachinesView currentUser={{ userId: 'u1', companyId: 'c1', role: 'company_admin' }} getAccessToken={async () => 'token'} />));
    await act(async () => container.querySelector('[role="button"]')?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    const open = [...container.querySelectorAll('button')].find(item => item.textContent === 'Demander l’ajout de documents')!;
    await act(async () => open.click());

    const input = document.body.querySelector('input[type="file"]') as HTMLInputElement;
    const inputClick = vi.spyOn(input, 'click').mockImplementation(() => undefined);
    const dropzone = [...document.body.querySelectorAll('[role="button"]')]
      .find(item => item.textContent?.includes('Déposez vos PDF'))!;
    await act(async () => {
      dropzone.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
      dropzone.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    expect(inputClick).toHaveBeenCalledOnce();
    expect(document.body.textContent).toContain('Demander l’ajout de documents');

    await act(async () => Object.defineProperty(input, 'files', { configurable: true, value: [] }));
    await act(async () => input.dispatchEvent(new Event('change', { bubbles: true })));
    expect(document.body.textContent).toContain('Demander l’ajout de documents');

    const file = new File(['pdf'], 'manuel.pdf', { type: 'application/pdf' });
    inspectPdfFilesMock.mockResolvedValue({ accepted: [{ id: 1, file, pageCount: 12 }], errors: [] });
    await act(async () => Object.defineProperty(input, 'files', { configurable: true, value: [file] }));
    await act(async () => input.dispatchEvent(new Event('change', { bubbles: true })));
    expect(document.body.textContent).toContain('manuel.pdf');
    expect(document.body.textContent).toContain('12 pages');
    expect(document.body.textContent).toContain('Demander l’ajout de documents');

    const close = [...document.body.querySelectorAll('button')].find(item => item.textContent === 'Fermer')!;
    await act(async () => close.click());
    expect(document.body.textContent).not.toContain('Déposez vos PDF');
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
