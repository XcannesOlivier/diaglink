import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import { act } from 'react';
import { UsersView } from '../UsersView';

const { getCompanyUsersMock, getUsersByCompanyMock, createTechnicianMock, createUserForCompanyMock, getUserMachineAccessMock, getUserMachineAccessForCompanyMock, replaceUserMachineAccessMock, replaceUserMachineAccessForCompanyMock, getCompaniesMock } = vi.hoisted(() => ({
  getCompanyUsersMock: vi.fn(),
  getUsersByCompanyMock: vi.fn(),
  createTechnicianMock: vi.fn(),
  createUserForCompanyMock: vi.fn(),
  getUserMachineAccessMock: vi.fn(),
  getUserMachineAccessForCompanyMock: vi.fn(),
  replaceUserMachineAccessMock: vi.fn(),
  replaceUserMachineAccessForCompanyMock: vi.fn(),
  getCompaniesMock: vi.fn(),
}));

vi.mock('../../../services/userService', () => ({
  getCompanyUsers: getCompanyUsersMock,
  getUsersByCompany: getUsersByCompanyMock,
  createTechnician: createTechnicianMock,
  createUserForCompany: createUserForCompanyMock,
  getUserMachineAccess: getUserMachineAccessMock,
  getUserMachineAccessForCompany: getUserMachineAccessForCompanyMock,
  replaceUserMachineAccess: replaceUserMachineAccessMock,
  replaceUserMachineAccessForCompany: replaceUserMachineAccessForCompanyMock,
}));

vi.mock('../../../services/companyService', () => ({
  getCompanies: getCompaniesMock,
}));

function setInputValue(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!;
  setter.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

// Unlike the old Dialog-based machine panel (which unmounted its content when closed), the inline
// expanded row panel stays in the DOM until the test's own container is torn down — without this,
// leftover containers from earlier tests would pollute document-wide queries in later tests.
const activeRoots: { root: Root; container: HTMLDivElement }[] = [];

afterEach(() => {
  for (const { root, container } of activeRoots.splice(0)) {
    act(() => {
      root.unmount();
    });
    container.remove();
  }
});

async function renderView(role: 'company_admin' | 'diaglink_super_admin' | 'technician' = 'company_admin'): Promise<HTMLDivElement> {
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  activeRoots.push({ root, container });
  await act(async () => {
    root.render(
      <UsersView
        currentUser={{ userId: 'admin1', companyId: 'c1', role }}
        getAccessToken={vi.fn().mockResolvedValue('token')}
      />
    );
  });
  return container;
}

describe('UsersView (company_admin)', () => {
  beforeEach(() => {
    getCompanyUsersMock.mockReset();
    getUsersByCompanyMock.mockReset();
    createTechnicianMock.mockReset();
    createUserForCompanyMock.mockReset();
    getUserMachineAccessMock.mockReset();
    getUserMachineAccessForCompanyMock.mockReset();
    replaceUserMachineAccessMock.mockReset();
    replaceUserMachineAccessForCompanyMock.mockReset();
    getCompaniesMock.mockReset();
    getCompanyUsersMock.mockResolvedValue({ kind: 'success', data: [] });
    getCompaniesMock.mockResolvedValue({ kind: 'success', data: [] });
  });

  it('super admin loads and saves machine access with the selected company id', async () => {
    getCompaniesMock.mockResolvedValue({ kind: 'success', data: [{ id: 'c1', name: 'Acme', status: 'active' }] });
    getUsersByCompanyMock.mockResolvedValue({ kind: 'success', data: [{ id: 'u1', email: 'tech@acme.test', role: 'technician', status: 'active' }] });
    getUserMachineAccessForCompanyMock.mockResolvedValue({ kind: 'success', data: [{ machineId: 'm1', name: 'Presse P1', assigned: true }] });
    replaceUserMachineAccessForCompanyMock.mockResolvedValue({ kind: 'success', data: null });

    const container = await renderView('diaglink_super_admin');
    const companyRow = Array.from(container.querySelectorAll('[role="button"]')).find(element => element.textContent?.includes('Acme'));
    await act(async () => {
      (companyRow as HTMLElement).click();
    });
    const userRow = Array.from(container.querySelectorAll('[role="button"]')).find(element => element.textContent?.includes('tech@acme.test'));
    await act(async () => {
      (userRow as HTMLElement).click();
    });

    expect(getUserMachineAccessForCompanyMock).toHaveBeenCalledWith(expect.any(Function), 'c1', 'u1');
    const saveButton = Array.from(document.querySelectorAll('button')).find(button => button.textContent === 'Enregistrer les accès');
    await act(async () => {
      saveButton!.click();
    });
    expect(replaceUserMachineAccessForCompanyMock).toHaveBeenCalledWith(expect.any(Function), 'c1', 'u1', ['m1']);

    const closeButton = Array.from(document.querySelectorAll('button')).find(button => button.textContent === 'Gérer');
    await act(async () => {
      closeButton!.click();
    });
  });

  it("affiche les utilisateurs renvoyés par l'API avec le rôle traduit", async () => {
    getCompanyUsersMock.mockResolvedValue({
      kind: 'success',
      data: [{ id: 'u1', email: 'tech@acme.test', role: 'technician', status: 'active' }],
    });

    const container = await renderView();

    expect(container.textContent).toContain('tech@acme.test');
    expect(container.textContent).toContain('Technicien');
    expect(getCompanyUsersMock).toHaveBeenCalledTimes(1);
  });

  it("n'appelle pas /api/company/users pour diaglink_super_admin", async () => {
    await renderView('diaglink_super_admin');

    expect(getCompanyUsersMock).not.toHaveBeenCalled();
  });

  it("affiche le bouton Ajouter un utilisateur et ouvre/ferme le dialog", async () => {
    const container = await renderView();

    const addButton = Array.from(container.querySelectorAll('button')).find(b => b.textContent === 'Ajouter un utilisateur');
    expect(addButton).toBeTruthy();

    await act(async () => {
      addButton!.click();
    });
    expect(document.body.textContent).toContain('Email');

    const cancelButton = Array.from(document.querySelectorAll('button')).find(b => b.textContent === 'Annuler');
    await act(async () => {
      cancelButton!.click();
    });
    expect(document.querySelector('input[type="email"]')).toBeNull();
  });

  it('crée un utilisateur avec succès et rafraîchit la liste', async () => {
    createTechnicianMock.mockResolvedValue({
      kind: 'success',
      data: { id: 'u2', email: 'new.tech@acme.test', role: 'technician', status: 'active' },
    });
    getUserMachineAccessMock.mockResolvedValue({ kind: 'success', data: [] });

    const container = await renderView();
    const addButton = Array.from(container.querySelectorAll('button')).find(b => b.textContent === 'Ajouter un utilisateur');
    await act(async () => {
      addButton!.click();
    });

    const [firstNameInput, lastNameInput, emailInput, phoneInput] = Array.from(document.querySelectorAll('input')) as HTMLInputElement[];
    await act(async () => {
      setInputValue(firstNameInput, 'Jean');
      setInputValue(lastNameInput, 'Dupont');
      setInputValue(emailInput, 'new.tech@acme.test');
      setInputValue(phoneInput, '0102030405');
    });

    getCompanyUsersMock.mockResolvedValue({
      kind: 'success',
      data: [{ id: 'u2', email: 'new.tech@acme.test', role: 'technician', status: 'active' }],
    });

    const submitButton = Array.from(document.querySelectorAll('button')).find(b => b.textContent === "Ajouter l'utilisateur");
    await act(async () => {
      submitButton!.click();
    });

    expect(createTechnicianMock).toHaveBeenCalledWith(expect.any(Function), {
      email: 'new.tech@acme.test',
      firstName: 'Jean',
      lastName: 'Dupont',
      phoneNumber: '0102030405',
      role: 'technician',
    });
    expect(document.body.textContent).toContain('Utilisateur ajouté.');
    expect(document.body.textContent).toContain('Machines autorisées — new.tech@acme.test');
    expect(getUserMachineAccessMock).toHaveBeenCalledWith(expect.any(Function), 'u2');

    const cancelButton = Array.from(document.querySelectorAll('button')).find(button => button.textContent === 'Annuler');
    await act(async () => {
      cancelButton!.click();
    });
    expect(document.body.textContent).toContain('Utilisateur ajouté.');
  });

  it('affiche une erreur 409 sans fermer le dialog quand l\'email existe déjà', async () => {
    createTechnicianMock.mockResolvedValue({ kind: 'conflict', message: 'Un utilisateur existe déjà avec cet email.' });

    const container = await renderView();
    const addButton = Array.from(container.querySelectorAll('button')).find(b => b.textContent === 'Ajouter un utilisateur');
    await act(async () => {
      addButton!.click();
    });

    const [firstNameInput, lastNameInput, emailInput, phoneInput] = Array.from(document.querySelectorAll('input')) as HTMLInputElement[];
    await act(async () => {
      setInputValue(firstNameInput, 'Jean');
      setInputValue(lastNameInput, 'Dupont');
      setInputValue(emailInput, 'dup@acme.test');
      setInputValue(phoneInput, '0102030405');
    });

    const submitButton = Array.from(document.querySelectorAll('button')).find(b => b.textContent === "Ajouter l'utilisateur");
    await act(async () => {
      submitButton!.click();
    });

    expect(document.body.textContent).toContain('Un utilisateur existe déjà avec cet email.');
    expect(document.querySelector('input[type="email"]')).not.toBeNull();
  });

  it('sélectionner un technicien affiche les machines avec les coches initiales du backend', async () => {
    getCompanyUsersMock.mockResolvedValue({
      kind: 'success',
      data: [{ id: 'u1', email: 'tech@acme.test', role: 'technician', status: 'active' }],
    });
    getUserMachineAccessMock.mockResolvedValue({
      kind: 'success',
      data: [
        { machineId: 'm1', name: 'Compresseur Atelier 1', reference: null, assigned: true },
        { machineId: 'm2', name: 'Groupe froid 02', reference: null, assigned: false },
      ],
    });

    const container = await renderView();
    const row = Array.from(container.querySelectorAll('[role="button"]')).find(el => el.textContent?.includes('tech@acme.test'));
    await act(async () => {
      (row as HTMLElement).click();
    });

    expect(document.body.textContent).toContain('Gestion des machines');
    const checkboxes = Array.from(document.querySelectorAll('input[type="checkbox"]')) as HTMLInputElement[];
    expect(checkboxes).toHaveLength(2);
    expect(checkboxes[0].checked).toBe(true);
    expect(checkboxes[1].checked).toBe(false);
  });

  it("modifier la sélection puis Enregistrer appelle replaceUserMachineAccess avec la liste complète", async () => {
    getCompanyUsersMock.mockResolvedValue({
      kind: 'success',
      data: [{ id: 'u1', email: 'tech@acme.test', role: 'technician', status: 'active' }],
    });
    getUserMachineAccessMock.mockResolvedValue({
      kind: 'success',
      data: [
        { machineId: 'm1', name: 'Compresseur Atelier 1', reference: null, assigned: true },
        { machineId: 'm2', name: 'Groupe froid 02', reference: null, assigned: false },
      ],
    });
    replaceUserMachineAccessMock.mockResolvedValue({ kind: 'success', data: null });

    const container = await renderView();
    const row = Array.from(container.querySelectorAll('[role="button"]')).find(el => el.textContent?.includes('tech@acme.test'));
    await act(async () => {
      (row as HTMLElement).click();
    });

    const checkboxes = Array.from(document.querySelectorAll('input[type="checkbox"]')) as HTMLInputElement[];
    await act(async () => {
      checkboxes[1].click();
    });

    const saveButton = Array.from(document.querySelectorAll('button')).find(b => b.textContent === 'Enregistrer les accès');
    await act(async () => {
      saveButton!.click();
    });

    expect(replaceUserMachineAccessMock).toHaveBeenCalledWith(expect.any(Function), 'u1', expect.arrayContaining(['m1', 'm2']));
    expect(document.body.textContent).toContain('Accès machines mis à jour.');
  });

  it('technician: ne voit jamais le bouton Ajouter un utilisateur ni la gestion des affectations', async () => {
    const container = await renderView('technician');

    expect(getCompanyUsersMock).not.toHaveBeenCalled();
    expect(container.textContent).not.toContain('Ajouter un utilisateur');
    expect(container.textContent).toContain('Accès non autorisé.');
  });
});

