import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import { act } from 'react';
import { CompaniesView } from '../CompaniesView';
vi.mock('../StripeCompanyPanel', () => ({ StripeCompanyPanel: () => null }));

const { getCompaniesMock, onboardCompanyMock } = vi.hoisted(() => ({
  getCompaniesMock: vi.fn(),
  onboardCompanyMock: vi.fn(),
}));

vi.mock('../../../services/companyService', () => ({
  getCompanies: getCompaniesMock,
  onboardCompany: onboardCompanyMock,
}));

function setInputValue(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!;
  setter.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

const mounted: { root: Root; container: HTMLDivElement }[] = [];
afterEach(async () => {
  for (const { root, container } of mounted.splice(0)) { await act(async () => root.unmount()); container.remove(); }
  vi.unstubAllGlobals();
});

async function fillRequiredForm() {
  const inputs = Array.from(document.querySelectorAll('input:not([type="email"]):not([type="file"])')) as HTMLInputElement[];
  const email = document.querySelector('input[type="email"]') as HTMLInputElement;
  const files = document.querySelector('input[type="file"]') as HTMLInputElement;
  await act(async () => {
    ['Acme Corp', 'Doe', 'John', '0123456789', 'Machine A'].forEach((value, i) => setInputValue(inputs[i], value));
    setInputValue(email, 'admin@acme.test');
    Object.defineProperty(files, 'files', { value: [new File(['%PDF'], 'test.pdf', { type: 'application/pdf' })], configurable: true });
    files.dispatchEvent(new Event('change', { bubbles: true }));
  });
}

async function renderView(): Promise<HTMLDivElement> {
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted.push({ root, container });
  await act(async () => {
    root.render(<CompaniesView getAccessToken={vi.fn().mockResolvedValue('token')} />);
  });
  return container;
}

describe('CompaniesView (super_admin)', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => [] }));
    getCompaniesMock.mockReset();
    onboardCompanyMock.mockReset();
    getCompaniesMock.mockResolvedValue({ kind: 'success', data: [] });
  });

  it('affiche la liste des entreprises', async () => {
    getCompaniesMock.mockResolvedValue({
      kind: 'success',
      data: [
        { id: 'c1', name: 'Acme Corp', status: 'active' },
        { id: 'c2', name: 'Globex', status: 'inactive' },
      ],
    });

    const container = await renderView();

    expect(container.textContent).toContain('Acme Corp');
    expect(container.textContent).toContain('Globex');
  });

  it('affiche un état vide quand aucune entreprise cliente', async () => {
    const container = await renderView();
    expect(container.textContent).toContain('Aucune entreprise cliente.');
  });

  it('affiche le bouton "Ajouter une entreprise" et ouvre/ferme le dialog', async () => {
    const container = await renderView();

    const addButton = Array.from(container.querySelectorAll('button')).find(b => b.textContent === 'Ajouter une entreprise');
    expect(addButton).toBeTruthy();

    await act(async () => {
      addButton!.click();
    });
    expect(document.body.textContent).toContain("Nom de l'entreprise");

    const cancelButton = Array.from(document.querySelectorAll('button')).find(b => b.textContent === 'Annuler');
    await act(async () => {
      cancelButton!.click();
    });
    expect(document.querySelector('input[type="email"]')).toBeNull();
  });

  it('valide les champs obligatoires avant soumission', async () => {
    const container = await renderView();

    const addButton = Array.from(container.querySelectorAll('button')).find(b => b.textContent === 'Ajouter une entreprise');
    await act(async () => {
      addButton!.click();
    });

    const submitButton = Array.from(document.querySelectorAll('button')).find(b => b.textContent === "Créer l'entreprise");
    await act(async () => {
      submitButton!.click();
    });

    expect(onboardCompanyMock).not.toHaveBeenCalled();
    expect(submitButton!.hasAttribute('disabled')).toBe(true);
  });

  it('affiche un état de chargement pendant la soumission puis met à jour la liste au succès', async () => {
    let resolveOnboard: (value: unknown) => void = () => {};
    onboardCompanyMock.mockReturnValue(new Promise(resolve => { resolveOnboard = resolve; }));

    const container = await renderView();
    const addButton = Array.from(container.querySelectorAll('button')).find(b => b.textContent === 'Ajouter une entreprise');
    await act(async () => {
      addButton!.click();
    });

    await fillRequiredForm();

    const submitButton = Array.from(document.querySelectorAll('button')).find(b => b.textContent === "Créer l'entreprise");

    act(() => {
      submitButton!.click();
    });

    // Loading: submit button disabled while awaiting the response
    expect(submitButton!.hasAttribute('disabled')).toBe(true);

    getCompaniesMock.mockResolvedValue({
      kind: 'success',
      data: [{ id: 'c1', name: 'Acme Corp', status: 'active' }],
    });

    await act(async () => {
      resolveOnboard({
        kind: 'success',
        data: { company: { id: 'c1', name: 'Acme Corp', status: 'active' }, admin: { id: 'u1', email: 'admin@acme.test', role: 'company_admin', status: 'active' } },
      });
    });

    expect(document.body.textContent).toContain('Entreprise créée.');
    expect(onboardCompanyMock).toHaveBeenCalledWith(expect.any(Function), 'Acme Corp', 'admin@acme.test', 'John', 'Doe', '0123456789');
  });

  it('affiche le message du conflit 409 sans fermer le dialog', async () => {
    onboardCompanyMock.mockResolvedValue({ kind: 'conflict', message: 'Une entreprise porte déjà ce nom.' });

    const container = await renderView();
    const addButton = Array.from(container.querySelectorAll('button')).find(b => b.textContent === 'Ajouter une entreprise');
    await act(async () => {
      addButton!.click();
    });

    await fillRequiredForm();

    const submitButton = Array.from(document.querySelectorAll('button')).find(b => b.textContent === "Créer l'entreprise");
    await act(async () => {
      submitButton!.click();
    });

    expect(document.body.textContent).toContain('Une entreprise porte déjà ce nom.');
    expect(document.body.textContent).toContain("Nom de l'entreprise");
  });

  it('upload les fichiers en FormData sans définir Content-Type manuellement', async () => {
    onboardCompanyMock.mockResolvedValue({
      kind: 'success',
      data: {
        company: { id: 'c1', name: 'Acme Corp', status: 'active' },
        admin: { id: 'u1', email: 'admin@acme.test', role: 'company_admin', status: 'active' },
      },
    });
    const fetchMock = vi.fn().mockResolvedValue({ ok: true });
    vi.stubGlobal('fetch', fetchMock);

    const container = await renderView();
    const addButton = Array.from(container.querySelectorAll('button')).find(b => b.textContent === 'Ajouter une entreprise');
    await act(async () => {
      addButton!.click();
    });

    const textInputs = Array.from(document.querySelectorAll('input:not([type="email"]):not([type="file"])')) as HTMLInputElement[];
    const emailInput = document.querySelector('input[type="email"]') as HTMLInputElement;
    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(['%PDF-1.4'], 'doc.pdf', { type: 'application/pdf' });

    await act(async () => {
      setInputValue(textInputs[0], 'Acme Corp');
      setInputValue(emailInput, 'admin@acme.test');
      setInputValue(textInputs[1], 'Doe');
      setInputValue(textInputs[2], 'John');
      setInputValue(textInputs[3], '0123456789');
      setInputValue(textInputs[4], 'Machine A');
      Object.defineProperty(fileInput, 'files', { value: [file], configurable: true });
      fileInput.dispatchEvent(new Event('change', { bubbles: true }));
    });

    const submitButton = Array.from(document.querySelectorAll('button')).find(b => b.textContent === "Créer l'entreprise");
    await act(async () => {
      submitButton!.click();
    });

    expect(fetchMock).toHaveBeenCalledWith('/api/files/upload', expect.objectContaining({
      method: 'POST',
      body: expect.any(FormData),
      headers: expect.not.objectContaining({ 'Content-Type': expect.any(String) }),
    }));
    const body = fetchMock.mock.calls[0][1].body as FormData;
    expect(body.get('companyName')).toBe('Acme Corp');
    expect(body.get('machineName')).toBe('Machine A');
    expect(body.getAll('files')).toEqual([file]);
  });
});

