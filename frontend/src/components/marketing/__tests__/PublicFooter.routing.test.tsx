import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it } from 'vitest';
import { PublicFooter } from '../ClosingSections';
import { APP_LOGIN_URL } from '../../../config/origins';

let root: Root | null = null;
let container: HTMLDivElement | null = null;

afterEach(async () => {
  if (root) await act(async () => root?.unmount());
  container?.remove();
  root = null;
  container = null;
});

describe('PublicFooter routing', () => {
  it('opens the login route in a new tab without an opener', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    await act(async () => {
      root?.render(<MemoryRouter><PublicFooter loginTarget={APP_LOGIN_URL} /></MemoryRouter>);
    });

    const loginLink = Array.from(container.querySelectorAll('a'))
      .find(link => link.textContent === 'Connexion');

    expect(loginLink?.getAttribute('href')).toBe(APP_LOGIN_URL);
    expect(loginLink?.getAttribute('target')).toBe('_blank');
    expect(loginLink?.getAttribute('rel')).toBe('noopener noreferrer');
  });

  it('opens the privacy page from the Confidentialité link', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    await act(async () => {
      root?.render(
        <MemoryRouter initialEntries={['/']}>
          <Routes>
            <Route path="/" element={<PublicFooter loginTarget="/login" />} />
            <Route path="/confidentialite" element={<h1>Politique de confidentialité</h1>} />
          </Routes>
        </MemoryRouter>,
      );
    });

    const privacyLink = Array.from(container.querySelectorAll('a'))
      .find(link => link.textContent === 'Confidentialité');

    expect(privacyLink?.getAttribute('href')).toBe('/confidentialite');

    await act(async () => {
      privacyLink?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    });

    expect(container.textContent).toContain('Politique de confidentialité');
  });

  it('opens the legal notice page from the Mentions légales link', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    await act(async () => {
      root?.render(
        <MemoryRouter initialEntries={['/']}>
          <Routes>
            <Route path="/" element={<PublicFooter loginTarget="/login" />} />
            <Route path="/mentions-legales" element={<h1>Mentions légales</h1>} />
          </Routes>
        </MemoryRouter>,
      );
    });

    const legalNoticeLink = Array.from(container.querySelectorAll('a'))
      .find(link => link.textContent === 'Mentions légales');

    expect(legalNoticeLink?.getAttribute('href')).toBe('/mentions-legales');

    await act(async () => {
      legalNoticeLink?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    });

    expect(container.textContent).toContain('Mentions légales');
  });

  it('opens the service conditions page from the Conditions générales de service link', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    await act(async () => {
      root?.render(
        <MemoryRouter initialEntries={['/']}>
          <Routes>
            <Route path="/" element={<PublicFooter loginTarget="/login" />} />
            <Route path="/conditions" element={<h1>Conditions générales de service</h1>} />
          </Routes>
        </MemoryRouter>,
      );
    });

    const conditionsLink = Array.from(container.querySelectorAll('a'))
      .find(link => link.textContent === 'Conditions générales de service');

    expect(conditionsLink?.getAttribute('href')).toBe('/conditions');

    await act(async () => {
      conditionsLink?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    });

    expect(container.textContent).toContain('Conditions générales de service');
  });
});
