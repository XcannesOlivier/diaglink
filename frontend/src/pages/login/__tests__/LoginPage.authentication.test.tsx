import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ComponentProps } from 'react';
import { LoginPage } from '../LoginPage';

type LoginPageProps = ComponentProps<typeof LoginPage>;

let root: Root | null = null;
let container: HTMLDivElement | null = null;

function Location() {
  const location = useLocation();
  return <span data-location>{location.pathname}</span>;
}

const defaultProps = (): LoginPageProps => ({
  isCheckingSession: false,
  email: '',
  setEmail: vi.fn(),
  emailCheckMessage: null,
  isCheckingEmail: false,
  showCodeStep: false,
  code: '',
  setCode: vi.fn(),
  codeCheckMessage: null,
  isVerifyingCode: false,
  handleContinue: vi.fn().mockResolvedValue(undefined),
  handleVerifyCode: vi.fn().mockResolvedValue(false),
  handleChangeEmail: vi.fn(),
});

async function renderLogin(overrides: Partial<LoginPageProps> = {}) {
  const props = { ...defaultProps(), ...overrides };
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  await act(async () => {
    root?.render(<MemoryRouter initialEntries={['/login']}><LoginPage {...props} /><Location /></MemoryRouter>);
  });
  return { container, props };
}

afterEach(async () => {
  if (root) await act(async () => root?.unmount());
  container?.remove();
  root = null;
  container = null;
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('LoginPage authentication form', () => {
  it('associe les libellés aux champs et conserve les aides de saisie mobile', async () => {
    const emailView = await renderLogin();
    const email = emailView.container.querySelector<HTMLInputElement>('#diaglink-login-email')!;
    expect(emailView.container.querySelector('label')?.htmlFor).toBe(email.id);
    expect(email.autocomplete).toBe('email');
    expect(email.inputMode).toBe('email');

    await act(async () => root?.unmount());
    emailView.container.remove();
    root = null;
    container = null;

    const codeView = await renderLogin({ showCodeStep: true });
    const code = codeView.container.querySelector<HTMLInputElement>('#diaglink-login-code')!;
    expect(codeView.container.querySelector('label')?.htmlFor).toBe(code.id);
    expect(code.autocomplete).toBe('one-time-code');
    expect(code.inputMode).toBe('numeric');
    expect(code.maxLength).toBe(6);
  });

  it('soumet l’adresse e-mail avec le formulaire utilisé par la touche Entrée', async () => {
    const handleContinue = vi.fn().mockResolvedValue(undefined);
    const view = await renderLogin({ email: 'tech@example.test', handleContinue });
    const form = view.container.querySelector('form')!;

    await act(async () => {
      form.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    });

    expect(handleContinue).toHaveBeenCalledTimes(1);
    expect(view.container.querySelector('button[type="submit"]')).not.toBeNull();
  });

  it('laisse la validation native refuser un e-mail invalide', async () => {
    const handleContinue = vi.fn().mockResolvedValue(undefined);
    const view = await renderLogin({ email: 'adresse-invalide', handleContinue });
    const form = view.container.querySelector<HTMLFormElement>('form')!;

    expect(form.checkValidity()).toBe(false);
    await act(async () => form.requestSubmit());
    expect(handleContinue).not.toHaveBeenCalled();
  });

  it('soumet un code complet avec le formulaire utilisé par la touche Entrée', async () => {
    const handleVerifyCode = vi.fn().mockResolvedValue(false);
    const view = await renderLogin({ showCodeStep: true, code: '123456', handleVerifyCode });

    await act(async () => {
      view.container.querySelector('form')?.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    });

    expect(handleVerifyCode).toHaveBeenCalledWith(undefined);
  });

  it('conserve la validation automatique dès le sixième chiffre', async () => {
    const setCode = vi.fn();
    const handleVerifyCode = vi.fn().mockResolvedValue(false);
    const view = await renderLogin({ showCodeStep: true, setCode, handleVerifyCode });
    const input = view.container.querySelector<HTMLInputElement>('#diaglink-login-code')!;
    const valueSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!;

    await act(async () => {
      valueSetter.call(input, '12a34 56');
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });

    expect(setCode).toHaveBeenCalledWith('123456');
    expect(handleVerifyCode).toHaveBeenCalledWith('123456');
  });

  it('conserve la validation automatique et masque le clavier sur mobile', async () => {
    vi.spyOn(window, 'matchMedia').mockReturnValue({
      matches: true,
      media: '(max-width: 1000px)',
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    });
    const handleVerifyCode = vi.fn().mockResolvedValue(false);
    const view = await renderLogin({ showCodeStep: true, handleVerifyCode });
    const input = view.container.querySelector<HTMLInputElement>('#diaglink-login-code')!;
    const valueSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!;
    input.focus();

    await act(async () => {
      valueSetter.call(input, '123456');
      input.dispatchEvent(new Event('input', { bubbles: true }));
      await new Promise(resolve => setTimeout(resolve, 150));
    });

    expect(handleVerifyCode).toHaveBeenCalledWith('123456');
    expect(document.activeElement).not.toBe(input);
  });

  it('ouvre l’application après une validation OTP réussie', async () => {
    const handleVerifyCode = vi.fn().mockResolvedValue(true);
    const view = await renderLogin({ showCodeStep: true, code: '123456', handleVerifyCode });

    await act(async () => {
      view.container.querySelector('form')?.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    });

    expect(view.container.querySelector('[data-location]')?.textContent).toBe('/app');
  });

  it('affiche l’erreur dans la page et un état visuel pendant la vérification', async () => {
    const view = await renderLogin({
      showCodeStep: true,
      code: '123456',
      codeCheckMessage: 'Code incorrect ou expiré.',
      isVerifyingCode: true,
    });
    const input = view.container.querySelector<HTMLInputElement>('#diaglink-login-code')!;
    const submit = view.container.querySelector<HTMLButtonElement>('button[type="submit"]')!;

    expect(view.container.querySelector('[role="alert"]')?.textContent).toBe('Code incorrect ou expiré.');
    expect(input.getAttribute('aria-describedby')).toBe('diaglink-login-code-message');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.disabled).toBe(true);
    expect(submit.disabled).toBe(true);
    expect(submit.textContent).toContain('Vérification...');
  });
});
