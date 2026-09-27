import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { HeroSection } from '../HeroSection';
import { APP_LOGIN_URL } from '../../../config/origins';

describe('HeroSection routing', () => {
  it('opens the login route in a new tab without an opener', async () => {
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    await act(async () => root.render(<MemoryRouter><HeroSection loginTarget={APP_LOGIN_URL} /></MemoryRouter>));

    const loginLink = [...container.querySelectorAll('a')]
      .find(link => link.textContent === 'Se connecter');
    expect(loginLink?.getAttribute('href')).toBe(APP_LOGIN_URL);
    expect(loginLink?.getAttribute('target')).toBe('_blank');
    expect(loginLink?.getAttribute('rel')).toBe('noopener noreferrer');

    await act(async () => root.unmount());
    container.remove();
  });
});
