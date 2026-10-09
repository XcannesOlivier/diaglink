import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useCompanyBrandingLogo } from '../useCompanyBrandingLogo';
import { initialAppState, type AppState } from '../../types/appState';

const mocks = vi.hoisted(() => ({
  state: null as AppState | null,
  dispatch: vi.fn(),
  getAccessToken: vi.fn().mockResolvedValue('token'),
  fetchCompanyLogo: vi.fn(),
}));

vi.mock('../../contexts/AppContext', () => ({
  useAppContext: () => ({
    state: mocks.state!,
    dispatch: mocks.dispatch,
  }),
}));
vi.mock('../useAuth', () => ({ useAuth: () => ({ getAccessToken: mocks.getAccessToken }) }));
vi.mock('../../services/companyBrandingService', () => ({ fetchCompanyLogo: mocks.fetchCompanyLogo }));

function stateWithLogo(hasLogo: boolean, logoVersion: string | null = null): AppState {
  return {
    ...initialAppState,
    auth: {
      ...initialAppState.auth,
      status: 'authenticated',
      currentUser: {
        userId: 'u1',
        companyId: 'c1',
        role: 'technician',
        companyBranding: { companyName: 'Acme', accentColor: null, hasLogo, logoVersion },
      },
    },
    branding: { ...initialAppState.branding },
  };
}

describe('useCompanyBrandingLogo', () => {
  let container: HTMLDivElement;
  let root: Root;
  let createObjectURL: ReturnType<typeof vi.fn>;
  let revokeObjectURL: ReturnType<typeof vi.fn>;

  function Harness() {
    useCompanyBrandingLogo();
    return null;
  }

  beforeEach(() => {
    mocks.dispatch.mockReset();
    mocks.getAccessToken.mockReset().mockResolvedValue('token');
    mocks.fetchCompanyLogo.mockReset();
    mocks.state = stateWithLogo(false);
    createObjectURL = vi.fn();
    revokeObjectURL = vi.fn();
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: createObjectURL });
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: revokeObjectURL });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
  });

  it('does not download when hasLogo is false', async () => {
    await act(async () => root.render(<Harness />));

    expect(mocks.fetchCompanyLogo).not.toHaveBeenCalled();
    expect(createObjectURL).not.toHaveBeenCalled();
  });

  it.each(['initializing', 'unauthenticated', 'authenticated'] as const)(
    'keeps an empty logo while currentUser is unresolved (%s)',
    async status => {
      mocks.state = {
        ...initialAppState,
        auth: { ...initialAppState.auth, status },
        branding: { ...initialAppState.branding },
      };

      await act(async () => root.render(<Harness />));

      expect(mocks.fetchCompanyLogo).not.toHaveBeenCalled();
      expect(createObjectURL).not.toHaveBeenCalled();
      expect(mocks.dispatch).not.toHaveBeenCalled();
    },
  );

  it('downloads after currentUser finishes loading', async () => {
    mocks.state = { ...initialAppState, branding: { ...initialAppState.branding } };
    await act(async () => root.render(<Harness />));
    expect(mocks.fetchCompanyLogo).not.toHaveBeenCalled();

    mocks.state = stateWithLogo(true, 'v1');
    mocks.fetchCompanyLogo.mockResolvedValue({ logo: new Blob(['logo']), diagLinkSessionExpired: false });
    createObjectURL.mockReturnValue('blob:company-v1');
    await act(async () => root.render(<Harness />));

    expect(mocks.fetchCompanyLogo).toHaveBeenCalledOnce();
    expect(mocks.dispatch).toHaveBeenCalledWith({
      type: 'COMPANY_LOGO_LOADED',
      companyId: 'c1',
      logoVersion: 'v1',
      logoObjectUrl: 'blob:company-v1',
    });
  });

  it('revokes the logo when currentUser is cleared', async () => {
    mocks.state = stateWithLogo(true, 'v1');
    mocks.fetchCompanyLogo.mockResolvedValue({ logo: new Blob(['logo']), diagLinkSessionExpired: false });
    createObjectURL.mockReturnValue('blob:company-v1');
    await act(async () => root.render(<Harness />));

    mocks.state = { ...initialAppState, branding: { ...initialAppState.branding } };
    await act(async () => root.render(<Harness />));

    expect(revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:company-v1');
    expect(mocks.dispatch).toHaveBeenLastCalledWith({ type: 'COMPANY_LOGO_CLEARED' });
    expect(mocks.fetchCompanyLogo).toHaveBeenCalledOnce();
  });

  it('uses the existing global logo without downloading and revokes it on unmount', async () => {
    mocks.state = {
      ...stateWithLogo(true, 'v1'),
      branding: { companyId: 'c1', logoVersion: 'v1', logoObjectUrl: 'blob:cached' },
    };
    await act(async () => root.render(<Harness />));

    expect(mocks.fetchCompanyLogo).not.toHaveBeenCalled();
    await act(async () => root.render(null));
    expect(revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:cached');
    expect(mocks.dispatch).toHaveBeenLastCalledWith({ type: 'COMPANY_LOGO_CLEARED' });
  });

  it('downloads once and publishes one Object URL', async () => {
    const blob = new Blob(['logo'], { type: 'image/png' });
    mocks.state = stateWithLogo(true, 'v1');
    mocks.fetchCompanyLogo.mockResolvedValue({ logo: blob, diagLinkSessionExpired: false });
    createObjectURL.mockReturnValue('blob:company-v1');

    await act(async () => {
      root.render(<Harness />);
      await Promise.resolve();
    });
    await act(async () => root.render(<Harness />));

    expect(mocks.fetchCompanyLogo).toHaveBeenCalledOnce();
    expect(createObjectURL).toHaveBeenCalledOnce();
    expect(createObjectURL).toHaveBeenCalledWith(blob);
    expect(mocks.dispatch).toHaveBeenCalledWith({
      type: 'COMPANY_LOGO_LOADED',
      companyId: 'c1',
      logoVersion: 'v1',
      logoObjectUrl: 'blob:company-v1',
    });
  });

  it('revokes the old Object URL when LogoVersion changes', async () => {
    mocks.state = stateWithLogo(true, 'v1');
    mocks.fetchCompanyLogo
      .mockResolvedValueOnce({ logo: new Blob(['old']), diagLinkSessionExpired: false })
      .mockResolvedValueOnce({ logo: new Blob(['new']), diagLinkSessionExpired: false });
    createObjectURL.mockReturnValueOnce('blob:company-v1').mockReturnValueOnce('blob:company-v2');

    await act(async () => {
      root.render(<Harness />);
      await Promise.resolve();
    });
    mocks.state = stateWithLogo(true, 'v2');
    await act(async () => {
      root.render(<Harness />);
      await Promise.resolve();
    });

    expect(mocks.fetchCompanyLogo).toHaveBeenCalledTimes(2);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:company-v1');
  });

  it('silently keeps the logo empty when the backend returns 404', async () => {
    mocks.state = stateWithLogo(true, 'v1');
    mocks.fetchCompanyLogo.mockResolvedValue({ logo: null, diagLinkSessionExpired: false });

    await act(async () => {
      root.render(<Harness />);
      await Promise.resolve();
    });

    expect(createObjectURL).not.toHaveBeenCalled();
    expect(mocks.dispatch).not.toHaveBeenCalledWith(expect.objectContaining({ type: 'COMPANY_LOGO_LOADED' }));
  });
});
