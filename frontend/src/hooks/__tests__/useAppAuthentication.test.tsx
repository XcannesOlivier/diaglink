import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useAppAuthentication } from '../useAppAuthentication';

const mocks = vi.hoisted(() => ({
  dispatch: vi.fn(),
  fetchCurrentUser: vi.fn(),
  setDiagLinkSession: vi.fn(),
  clearDiagLinkSession: vi.fn(),
}));

vi.mock('@azure/msal-react', () => ({ useMsal: () => ({ accounts: [] }) }));
vi.mock('../useAppState', () => ({
  useAppState: () => ({ auth: { currentUser: null }, dispatch: mocks.dispatch }),
}));
vi.mock('../useAuth', () => ({ useAuth: () => ({ getAccessToken: vi.fn() }) }));
vi.mock('../../services/currentUserService', () => ({ fetchCurrentUser: mocks.fetchCurrentUser }));
vi.mock('../../utils/apiAuth', () => ({
  clearDiagLinkSession: mocks.clearDiagLinkSession,
  getDiagLinkSessionExpiresAt: () => null,
  getDiagLinkSessionToken: () => null,
  setDiagLinkSession: mocks.setDiagLinkSession,
}));

type Authentication = ReturnType<typeof useAppAuthentication>;
let latestAuthentication: Authentication | null = null;
let root: Root | null = null;
let container: HTMLDivElement | null = null;

function Harness() {
  latestAuthentication = useAppAuthentication();
  return null;
}

function authentication(): Authentication {
  if (!latestAuthentication) throw new Error('Authentication hook is not mounted.');
  return latestAuthentication;
}

async function renderHook() {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  await act(async () => root?.render(<Harness />));
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  latestAuthentication = null;
  mocks.dispatch.mockReset();
  mocks.fetchCurrentUser.mockReset().mockResolvedValue({
    currentUser: { id: 'user-1', email: 'tech@example.test', role: 'technician', status: 'active' },
    diagLinkSessionExpired: false,
  });
  mocks.setDiagLinkSession.mockReset();
  mocks.clearDiagLinkSession.mockReset();
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(async () => {
  if (root) await act(async () => root?.unmount());
  container?.remove();
  root = null;
  container = null;
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

async function setEmail(value = 'tech@example.test') {
  await act(async () => authentication().setEmail(value));
}

describe('useAppAuthentication OTP feedback', () => {
  it('conserve une réponse générique et ouvre l’étape OTP pour toute adresse acceptée publiquement', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ known: true }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ success: true }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await renderHook();
    await setEmail('unknown@example.test');

    await act(async () => authentication().handleContinue());

    expect(authentication().showCodeStep).toBe(true);
    expect(authentication().emailCheckMessage).toBeNull();
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls[1][0]).toBe('/api/auth/request-code');
  });

  it('affiche dans la page un code incorrect ou expiré', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ success: false }), { status: 200 })));
    await renderHook();
    await setEmail();

    await act(async () => { await authentication().handleVerifyCode('123456'); });

    expect(authentication().codeCheckMessage).toBe('Code incorrect ou expiré.');
    expect(authentication().isVerifyingCode).toBe(false);
  });

  it('affiche dans la page une erreur réseau', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Network error')));
    await renderHook();
    await setEmail();

    await act(async () => { await authentication().handleVerifyCode('123456'); });

    expect(authentication().codeCheckMessage).toBe('Impossible de vérifier le code. Veuillez réessayer.');
  });

  it('affiche la limitation 429 sans modifier les règles anti-abus', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 429 })));
    await renderHook();
    await setEmail();

    await act(async () => { await authentication().handleVerifyCode('123456'); });

    expect(authentication().codeCheckMessage).toBe('Trop de tentatives. Veuillez réessayer plus tard.');
  });

  it('affiche aussi la limitation 429 lors de la demande du code', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ known: true }), { status: 200 }))
      .mockResolvedValueOnce(new Response(null, { status: 429 }));
    vi.stubGlobal('fetch', fetchMock);
    await renderHook();
    await setEmail();

    await act(async () => authentication().handleContinue());

    expect(authentication().showCodeStep).toBe(false);
    expect(authentication().emailCheckMessage).toBe('Trop de tentatives. Veuillez réessayer plus tard.');
  });

  it('empêche deux vérifications simultanées du même code', async () => {
    let resolveResponse: (response: Response) => void = () => undefined;
    const response = new Promise<Response>(resolve => { resolveResponse = resolve; });
    const fetchMock = vi.fn().mockReturnValue(response);
    vi.stubGlobal('fetch', fetchMock);
    await renderHook();
    await setEmail();

    let firstVerification!: Promise<boolean>;
    let secondVerification!: Promise<boolean>;
    await act(async () => {
      firstVerification = authentication().handleVerifyCode('123456');
      secondVerification = authentication().handleVerifyCode('123456');
      await Promise.resolve();
    });

    expect(authentication().isVerifyingCode).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    await act(async () => {
      resolveResponse(new Response(JSON.stringify({ success: false }), { status: 200 }));
      await firstVerification;
    });
    expect(await secondVerification).toBe(false);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('conserve la création de session après un code valide', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      success: true,
      sessionToken: 'session-token',
      expiresAtUtc: '2026-10-25T12:00:00Z',
    }), { status: 200 })));
    await renderHook();
    await setEmail();

    let success = false;
    await act(async () => { success = await authentication().handleVerifyCode('123456'); });

    expect(success).toBe(true);
    expect(mocks.setDiagLinkSession).toHaveBeenCalledWith('session-token', '2026-10-25T12:00:00Z');
    expect(mocks.fetchCurrentUser).toHaveBeenCalledTimes(1);
    expect(authentication().isAuthenticated).toBe(true);
  });
});
