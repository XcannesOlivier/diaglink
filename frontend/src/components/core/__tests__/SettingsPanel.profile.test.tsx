import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SettingsPanel } from '../SettingsPanel';

const updateCompanyUserMock = vi.hoisted(() => vi.fn());
vi.mock('../../../services/userService', () => ({ updateCompanyUser: updateCompanyUserMock }));

let root: Root | null = null;
let container: HTMLDivElement | null = null;

afterEach(() => {
  if (root) act(() => root!.unmount());
  container?.remove();
  root = null;
  container = null;
});

beforeEach(() => updateCompanyUserMock.mockReset());

function setInputValue(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!;
  setter.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

async function render(role: 'company_admin' | 'technician' | 'diaglink_super_admin' = 'company_admin', refresh = vi.fn(), email = 'admin@acme.test',
  openPersonalization = vi.fn(), openChange = vi.fn()) {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  await act(async () => root!.render(
    <SettingsPanel
      isOpen
      onOpenChange={openChange}
      currentUser={{ userId: 'admin1', companyId: 'c1', role, email, firstName: 'Jean', lastName: 'Dupont', phoneNumber: '000' }}
      getAccessToken={vi.fn().mockResolvedValue('token')}
      onCurrentUserRefresh={refresh}
      onOpenPersonalization={openPersonalization}
    />
  ));
  return refresh;
}

describe('SettingsPanel profile editing', () => {
  it('opens personalization for the company administrator and closes settings', async () => {
    const open = vi.fn();
    const close = vi.fn();
    await render('company_admin', vi.fn(), 'admin@acme.test', open, close);
    const entry = Array.from(document.querySelectorAll('button')).find(button => button.textContent === 'Personnalisation')!;
    expect(entry.querySelector('svg')).not.toBeNull();
    await act(async () => entry.click());
    expect(close).toHaveBeenCalledWith(false);
    expect(open).toHaveBeenCalledOnce();
  });

  it.each(['technician', 'diaglink_super_admin'] as const)('does not expose personalization for %s', async role => {
    await render(role);
    expect(document.body.textContent).not.toContain('Personnalisation');
  });
  it('groups personalization below the separator and before profile editing with consistent spacing', async () => {
    await render();
    const buttons = Array.from(document.querySelectorAll('button'));
    const personalization = buttons.find(button => button.textContent === 'Personnalisation')!;
    const edit = buttons.find(button => button.textContent === 'Modifier mes informations')!;
    const area = personalization.parentElement!;
    expect(area.contains(edit)).toBe(true);
    expect(area.firstElementChild).toBe(personalization);
    expect(personalization.nextElementSibling?.contains(edit)).toBe(true);
    expect(area.textContent).not.toContain('Apparence');
    const styles = getComputedStyle(area);
    expect(styles.paddingTop).toBe('24px');
    expect(styles.paddingBottom).toBe('24px');
    expect(styles.display).toBe('flex');
    expect(styles.flexDirection).toBe('column');
    expect(styles.rowGap).toBe('var(--spacingVerticalM)');
  });
  it('place l’action de profil dans le flux avec 24 px au-dessus et en dessous', () => {
    const source = readFileSync(resolve('src/components/core/SettingsPanel.tsx'), 'utf8');
    expect(source).toContain("paddingTop: '24px'");
    expect(source).toContain("paddingBottom: '24px'");
    expect(source).toContain('borderTop: `1px solid ${tokens.colorNeutralStroke2}`');
    expect(source).toContain("overflowY: 'auto'");
    expect(source).not.toContain("marginTop: 'auto'");
  });

  it('permet à un administrateur entreprise de modifier ses informations et rafraîchit son identité', async () => {
    const refresh = vi.fn().mockResolvedValue(undefined);
    updateCompanyUserMock.mockResolvedValue({
      kind: 'success',
      data: { id: 'admin1', email: 'admin@acme.test', role: 'company_admin', status: 'active', firstName: 'Alice', lastName: 'Martin', phoneNumber: '123' },
    });
    await render('company_admin', refresh);
    const edit = Array.from(document.querySelectorAll('button')).find(button => button.textContent?.includes('Modifier mes informations'));
    expect(edit?.querySelector('svg')).not.toBeNull();
    await act(async () => edit!.click());
    const inputs = Array.from(document.querySelectorAll('[role="dialog"] input')) as HTMLInputElement[];
    expect(inputs).toHaveLength(3);
    await act(async () => {
      setInputValue(inputs[0], ' Alice ');
      setInputValue(inputs[1], ' Martin ');
      setInputValue(inputs[2], ' 123 ');
    });
    const save = Array.from(document.querySelectorAll('[role="dialog"] button')).find(button => button.textContent === 'Enregistrer');
    await act(async () => (save as HTMLButtonElement).click());

    expect(updateCompanyUserMock).toHaveBeenCalledWith(expect.any(Function), 'admin1', {
      firstName: 'Alice', lastName: 'Martin', phoneNumber: '123',
    });
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it("affiche l'e-mail authentifié et l'actualise lorsque le compte rafraîchi change", async () => {
    const refresh = vi.fn().mockResolvedValue(undefined);
    await render('company_admin', refresh, 'ancienne.adresse.tres.longue@acme.test');
    expect(document.body.textContent).toContain('ancienne.adresse.tres.longue@acme.test');

    await act(async () => root!.render(
      <SettingsPanel
        isOpen
        onOpenChange={vi.fn()}
        currentUser={{ userId: 'admin1', companyId: 'c1', role: 'company_admin', email: 'nouvelle@acme.test' }}
        getAccessToken={vi.fn().mockResolvedValue('token')}
        onCurrentUserRefresh={refresh}
        onOpenPersonalization={vi.fn()}
      />
    ));

    expect(document.body.textContent).toContain('nouvelle@acme.test');
    expect(document.body.textContent).not.toContain('ancienne.adresse.tres.longue@acme.test');
    expect(document.body.textContent).toContain('Modifier mes informations');
  });

  it('ne propose pas la modification personnelle à un technicien', async () => {
    await render('technician');
    expect(document.body.textContent).not.toContain('Modifier mes informations');
  });
});
