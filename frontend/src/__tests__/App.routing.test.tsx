import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../App';

const authentication = {
  isAuthenticated: false,
  isCheckingSession: false,
  email: '',
  setEmail: vi.fn(),
  emailCheckMessage: null,
  isCheckingEmail: false,
  showCodeStep: false,
  code: '',
  setCode: vi.fn(),
  handleContinue: vi.fn(),
  handleVerifyCode: vi.fn(),
  handleChangeEmail: vi.fn(),
  expireDiagLinkSession: vi.fn(),
};

vi.mock('../hooks/useAppAuthentication', () => ({ useAppAuthentication: () => authentication }));
vi.mock('../pages/landing/LandingPage', () => ({ LandingPage: () => <div>landing-page</div> }));
vi.mock('../pages/login/LoginPage', () => ({ LoginPage: () => <div>login-page</div> }));
vi.mock('../components/AuthenticatedApp', () => ({ AuthenticatedApp: ({ loadingOnly }: { loadingOnly?: boolean }) => <div>{loadingOnly ? 'session-loading' : 'authenticated-app'}</div> }));

let root: Root | null = null;
let container: HTMLDivElement | null = null;
const scrollTo = vi.fn();

async function renderAt(path: string) {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  await act(async () => {
    root?.render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>);
  });
}

afterEach(async () => {
  if (root) await act(async () => root?.unmount());
  container?.remove();
  root = null;
  container = null;
  vi.unstubAllGlobals();
});

beforeEach(() => {
  authentication.isAuthenticated = false;
  authentication.isCheckingSession = false;
  vi.stubGlobal('scrollTo', scrollTo);
  scrollTo.mockClear();
});

describe('main application routes', () => {
  it('always renders the public landing page at /', async () => {
    authentication.isAuthenticated = true;
    await renderAt('/');
    expect(container?.textContent).toContain('landing-page');
  });

  it('renders login for an anonymous visitor', async () => {
    await renderAt('/login');
    expect(container?.textContent).toContain('login-page');
  });

  it('renders the public privacy page directly', async () => {
    await renderAt('/confidentialite');
    expect(container?.textContent).toContain('Politique de confidentialité');
  });

  it('renders the public legal notice page directly', async () => {
    await renderAt('/mentions-legales');
    expect(container?.textContent).toContain('Mentions légales');
  });

  it('renders the public professional service conditions directly', async () => {
    await renderAt('/conditions');
    expect(container?.textContent).toContain('Conditions générales de service');
    expect(container?.textContent).toContain('Clients professionnels');
    expect(container?.textContent).toContain('29,90 € HT par mois et par machine active');
    expect(container?.textContent).toContain('14. Litiges et contact');
    expect(container?.textContent).not.toContain('login-page');

    const privacyLink = [...container!.querySelectorAll('a')]
      .find(link => link.textContent === 'Politique de confidentialité de DiagLink');
    expect(privacyLink?.getAttribute('href')).toBe('/confidentialite');
  });

  it('scrolls to the top when navigating normally to the service conditions', async () => {
    await renderAt('/confidentialite');
    expect(scrollTo).not.toHaveBeenCalled();

    const conditionsLink = [...container!.querySelectorAll('a')]
      .find(link => link.textContent === 'Conditions générales de service');
    await act(async () => conditionsLink?.click());

    expect(container?.textContent).toContain('Conditions générales de service');
    expect(scrollTo).toHaveBeenCalledExactlyOnceWith({ top: 0, left: 0, behavior: 'auto' });
  });

  it('renders the public demonstration page directly', async () => {
    await renderAt('/demonstration');
    expect(container?.textContent).toContain('Découvrez DiagLink en situation réelle.');
    const setupLink = [...container!.querySelectorAll('a')]
      .find(link => link.textContent === 'Configurer ma première machine');
    expect(setupLink?.getAttribute('href')).toBe('/commencer');
  });

  it('renders the public first-machine setup page directly', async () => {
    await renderAt('/commencer');
    expect(container?.textContent).toContain('Configurez votre première machine.');
  });

  it('routes the Checkout return outside the authenticated application shell', async () => {
    await renderAt('/app/checkout-return');
    expect(container?.textContent).not.toContain('authenticated-app');
    expect(container?.textContent).not.toContain('login-page');
    expect(container?.textContent).not.toContain('Configurez votre');
  });

  it('redirects anonymous protected routes to login', async () => {
    await renderAt('/app');
    expect(container?.textContent).toContain('login-page');
  });

  it('waits for session restoration on a protected route', async () => {
    authentication.isCheckingSession = true;
    await renderAt('/app');
    expect(container?.textContent).toContain('session-loading');
  });

  it('renders the authenticated application including administration', async () => {
    authentication.isAuthenticated = true;
    await renderAt('/app/administration');
    expect(container?.textContent).toContain('authenticated-app');
  });

  it('keeps the legacy administration alias compatible', async () => {
    authentication.isAuthenticated = true;
    await renderAt('/administration');
    expect(container?.textContent).toContain('authenticated-app');
  });

  it('redirects an authenticated login visit to the app', async () => {
    authentication.isAuthenticated = true;
    await renderAt('/login');
    expect(container?.textContent).toContain('authenticated-app');
  });

  it('keeps an authenticated installation request on the login page', async () => {
    authentication.isAuthenticated = true;
    await renderAt('/login?install=1');
    expect(container?.textContent).toContain('login-page');
    expect(container?.textContent).not.toContain('authenticated-app');
  });
});
