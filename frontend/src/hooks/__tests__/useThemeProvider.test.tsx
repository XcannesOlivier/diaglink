import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ThemeContextValue } from '../../contexts/ThemeContext';
import { darkTheme, lightTheme } from '../../config/themes';
import { useThemeProvider } from '../useThemeProvider';

let root: Root | null = null;
let container: HTMLDivElement | null = null;
let currentTheme: ThemeContextValue | null = null;
let prefersDark = false;
const mediaListeners = new Set<(event: MediaQueryListEvent) => void>();

function ThemeProbe() {
  currentTheme = useThemeProvider();
  return null;
}

function theme(): ThemeContextValue {
  if (!currentTheme) throw new Error('Theme provider is not mounted.');
  return currentTheme;
}

function mediaQueryList(): MediaQueryList {
  return {
    get matches() { return prefersDark; },
    media: '(prefers-color-scheme: dark)',
    onchange: null,
    addListener: () => undefined,
    removeListener: () => undefined,
    addEventListener: (_type: string, listener: EventListenerOrEventListenerObject) => {
      if (typeof listener === 'function') mediaListeners.add(listener as (event: MediaQueryListEvent) => void);
    },
    removeEventListener: (_type: string, listener: EventListenerOrEventListenerObject) => {
      if (typeof listener === 'function') mediaListeners.delete(listener as (event: MediaQueryListEvent) => void);
    },
    dispatchEvent: () => true,
  } as unknown as MediaQueryList;
}

async function renderHook() {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  await act(async () => root?.render(<ThemeProbe />));
}

async function setDeviceDarkMode(value: boolean) {
  prefersDark = value;
  await act(async () => {
    const event = { matches: value, media: '(prefers-color-scheme: dark)' } as MediaQueryListEvent;
    mediaListeners.forEach(listener => listener(event));
  });
}

beforeEach(() => {
  localStorage.clear();
  prefersDark = false;
  mediaListeners.clear();
  currentTheme = null;
  vi.spyOn(window, 'matchMedia').mockImplementation(() => mediaQueryList());
});

afterEach(async () => {
  if (root) await act(async () => root?.unmount());
  container?.remove();
  root = null;
  container = null;
  currentTheme = null;
  localStorage.clear();
  document.documentElement.style.colorScheme = '';
  vi.restoreAllMocks();
});

describe('useThemeProvider System mode', () => {
  it('utilise Système par défaut et suit les changements de l’appareil', async () => {
    await renderHook();

    expect(theme().savedTheme).toBe('System');
    expect(theme().currentTheme).toBe('Light');
    expect(theme().themeStyles).toBe(lightTheme);
    expect(document.documentElement.style.colorScheme).toBe('light');

    await setDeviceDarkMode(true);

    expect(theme().savedTheme).toBe('System');
    expect(theme().currentTheme).toBe('Dark');
    expect(theme().isDarkMode).toBe(true);
    expect(theme().themeStyles).toBe(darkTheme);
    expect(document.documentElement.style.colorScheme).toBe('dark');
  });

  it('mémorise Système et ignore l’appareil quand Clair ou Sombre est imposé', async () => {
    localStorage.setItem('ai-foundry-theme', 'Dark');
    await renderHook();
    expect(theme().currentTheme).toBe('Dark');

    await act(async () => theme().setTheme('Light'));
    expect(localStorage.getItem('ai-foundry-theme')).toBe('Light');
    await setDeviceDarkMode(true);
    expect(theme().currentTheme).toBe('Light');

    await act(async () => theme().setTheme('System'));
    expect(localStorage.getItem('ai-foundry-theme')).toBe('System');
    expect(theme().currentTheme).toBe('Dark');
  });
});
