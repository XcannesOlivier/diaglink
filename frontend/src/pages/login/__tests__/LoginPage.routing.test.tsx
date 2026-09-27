import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { LoginPage } from '../LoginPage';
import { PUBLIC_ORIGIN } from '../../../config/origins';

function Location() {
  const location = useLocation();
  return <span data-location>{location.pathname}{location.search}</span>;
}

describe('LoginPage routing', () => {
  it('returns to the public home page in the same tab', async () => {
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={['/login']}>
          <LoginPage
            isCheckingSession={false}
            email=""
            setEmail={() => undefined}
            emailCheckMessage={null}
            isCheckingEmail={false}
            showCodeStep={false}
            code=""
            setCode={() => undefined}
            handleContinue={async () => undefined}
            handleVerifyCode={async () => false}
            handleChangeEmail={() => undefined}
          />
          <Location />
        </MemoryRouter>,
      );
    });

    const homeLink = [...container.querySelectorAll('a')]
      .find(link => link.textContent === 'Site DiagLink');
    expect(homeLink?.getAttribute('href')).toBe(`${PUBLIC_ORIGIN}/`);
    expect(homeLink?.getAttribute('target')).toBeNull();

    await act(async () => root.unmount());
    container.remove();
  });

  it('opens shortcut instructions on the login page without another navigation action', async () => {
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={['/login']}>
          <LoginPage
            isCheckingSession={false}
            email=""
            setEmail={() => undefined}
            emailCheckMessage={null}
            isCheckingEmail={false}
            showCodeStep={false}
            code=""
            setCode={() => undefined}
            handleContinue={async () => undefined}
            handleVerifyCode={async () => false}
            handleChangeEmail={() => undefined}
          />
          <Location />
        </MemoryRouter>,
      );
    });

    const installButton = [...container.querySelectorAll('button')]
      .find(button => button.textContent === 'Installer DiagLink');
    await act(async () => installButton?.click());

    expect(document.body.textContent).toContain('Créez maintenant un raccourci vers cette page de connexion.');
    expect([...document.body.querySelectorAll('button')]
      .some(button => button.textContent === 'Ouvrir DiagLink')).toBe(false);
    expect(container.querySelector('[data-location]')?.textContent).toBe('/login');

    await act(async () => root.unmount());
    container.remove();
  });

  it('opens requested shortcut instructions and removes install from the URL when closed', async () => {
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={['/login?install=1&source=landing']}>
          <LoginPage
            isCheckingSession={false}
            email=""
            setEmail={() => undefined}
            emailCheckMessage={null}
            isCheckingEmail={false}
            showCodeStep={false}
            code=""
            setCode={() => undefined}
            handleContinue={async () => undefined}
            handleVerifyCode={async () => false}
            handleChangeEmail={() => undefined}
          />
          <Location />
        </MemoryRouter>,
      );
    });

    expect(document.body.textContent).toContain('Créez maintenant un raccourci vers cette page de connexion.');
    expect(container.querySelector('[data-location]')?.textContent).toBe('/login?install=1&source=landing');

    const closeButton = [...document.body.querySelectorAll('button')]
      .find(button => button.textContent === 'Fermer');
    await act(async () => closeButton?.click());

    expect(document.body.textContent).not.toContain('Créez maintenant un raccourci vers cette page de connexion.');
    expect(container.querySelector('[data-location]')?.textContent).toBe('/login?source=landing');

    await act(async () => root.unmount());
    container.remove();
  });
});
