import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ScrollToTop } from '../ScrollToTop';

function NavigationHarness({ target }: { target: string }) {
  const navigate = useNavigate();
  return <>
    <ScrollToTop />
    <button onClick={() => navigate(target)}>Changer de page</button>
    <button onClick={() => navigate('/#tarifs')}>Voir les tarifs</button>
  </>;
}

describe('ScrollToTop', () => {
  let root: Root;
  let container: HTMLDivElement;
  const scrollTo = vi.fn();

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    vi.stubGlobal('scrollTo', scrollTo);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
    vi.clearAllMocks();
  });

  it.each(['/', '/commencer', '/contact', '/confidentialite', '/demonstration', '/mentions-legales'])
    ('scrolls to the top when navigating to public path %s', async target => {
      await act(async () => root.render(
        <MemoryRouter initialEntries={['/source']}>
          <NavigationHarness target={target} />
        </MemoryRouter>,
      ));
      expect(scrollTo).not.toHaveBeenCalled();

      await act(async () => container.querySelector<HTMLButtonElement>('button')!.click());
      expect(scrollTo).toHaveBeenCalledExactlyOnceWith({ top: 0, left: 0, behavior: 'auto' });
    });

  it('does not scroll to the top for a hash change on the landing page', async () => {
    await act(async () => root.render(
      <MemoryRouter initialEntries={['/']}>
        <NavigationHarness target="/commencer" />
      </MemoryRouter>,
    ));
    expect(scrollTo).not.toHaveBeenCalled();

    await act(async () => container.querySelectorAll<HTMLButtonElement>('button')[1].click());
    expect(scrollTo).not.toHaveBeenCalled();
  });
});
