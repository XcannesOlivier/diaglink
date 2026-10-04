import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ThemeContext, type ThemeContextValue } from '../../../contexts/ThemeContext';
import { darkTheme, lightTheme } from '../../../config/themes';
import lightLogo from '../../../assets/Logo DiagLink.png';
import darkLogo from '../../../assets/Logo DiagLink Sombre.png';
import { DiagLinkLogo } from '../DiagLinkLogo';

const themeValue = (isDarkMode: boolean): ThemeContextValue => ({
  savedTheme: isDarkMode ? 'Dark' : 'Light',
  currentTheme: isDarkMode ? 'Dark' : 'Light',
  themeStyles: isDarkMode ? darkTheme : lightTheme,
  setTheme: vi.fn(),
  isDarkMode,
});

describe('DiagLinkLogo', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
  });

  it('updates the logo immediately when the active theme changes', async () => {
    await act(async () => root.render(
      <ThemeContext.Provider value={themeValue(false)}>
        <DiagLinkLogo className="existing-logo-style" />
      </ThemeContext.Provider>,
    ));

    const logo = container.querySelector<HTMLImageElement>('img[alt="DiagLink"]');
    expect(logo?.getAttribute('src')).toBe(lightLogo);
    expect(logo?.className).toBe('existing-logo-style');

    await act(async () => root.render(
      <ThemeContext.Provider value={themeValue(true)}>
        <DiagLinkLogo className="existing-logo-style" />
      </ThemeContext.Provider>,
    ));

    expect(logo?.getAttribute('src')).toBe(darkLogo);
    expect(logo?.className).toBe('existing-logo-style');
  });
});
