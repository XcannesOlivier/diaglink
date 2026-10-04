import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { HeroSection } from '../HeroSection';
import { APP_LOGIN_URL } from '../../../config/origins';

describe('HeroSection routing', () => {
  it('renders the hero title as naturally wrapping text with only the highlighted word isolated', async () => {
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    await act(async () => root.render(<MemoryRouter><HeroSection loginTarget={APP_LOGIN_URL} /></MemoryRouter>));

    const title = container.querySelector('h1');
    expect(title?.textContent).toBe('La documentation de vos machines devient interactive.');
    expect(title?.querySelector('br')).toBeNull();
    expect(title?.querySelectorAll('span')).toHaveLength(1);
    expect(title?.querySelector('span')?.textContent).toBe('interactive.');

    await act(async () => root.unmount());
    container.remove();
  });

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
