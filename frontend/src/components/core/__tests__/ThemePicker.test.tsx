import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { FluentProvider } from '@fluentui/react-components';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ThemeContext, type ThemeType } from '../../../contexts/ThemeContext';
import { lightTheme } from '../../../config/themes';
import { ThemePicker } from '../ThemePicker';

let root: Root | null = null;
let container: HTMLDivElement | null = null;

async function renderPicker(savedTheme: ThemeType, setTheme = vi.fn()) {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  await act(async () => {
    root?.render(
      <ThemeContext.Provider value={{
        savedTheme,
        currentTheme: 'Light',
        themeStyles: lightTheme,
        setTheme,
        isDarkMode: false,
      }}>
        <FluentProvider theme={lightTheme}>
          <ThemePicker />
        </FluentProvider>
      </ThemeContext.Provider>,
    );
  });
  return { container, setTheme };
}

afterEach(async () => {
  if (root) await act(async () => root?.unmount());
  container?.remove();
  document.querySelectorAll('[data-portal-node]').forEach(node => node.remove());
  root = null;
  container = null;
});

describe('ThemePicker', () => {
  it('affiche Système lorsque cette préférence est enregistrée', async () => {
    const view = await renderPicker('System');
    const dropdown = view.container.querySelector<HTMLElement>('[role="combobox"]')!;

    expect(dropdown.textContent).toContain('Système');
    expect(view.container.querySelector('label')?.htmlFor).toBe(dropdown.id);
  });

  it('propose Clair, Sombre et Système et permet de sélectionner Système', async () => {
    const setTheme = vi.fn();
    const view = await renderPicker('Light', setTheme);
    const dropdown = view.container.querySelector<HTMLElement>('[role="combobox"]')!;

    await act(async () => dropdown.click());
    const options = Array.from(document.body.querySelectorAll<HTMLElement>('[role="option"]'));
    expect(options.map(option => option.textContent)).toEqual(['Clair', 'Sombre', 'Système']);

    await act(async () => options[2].click());
    expect(setTheme).toHaveBeenCalledWith('System');
  });
});
