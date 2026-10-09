import { act, type CSSProperties } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import {
  Badge, Button, Checkbox, Dialog, DialogBody, DialogSurface, DialogTitle, FluentProvider,
  Input, Link, Radio, Spinner, Switch, Tab, TabList, useThemeClassName,
} from '@fluentui/react-components';
import { DeleteRegular, SettingsRegular } from '@fluentui/react-icons';
import { afterEach, describe, expect, it } from 'vitest';
import { CompanyAccentProvider } from '../CompanyAccentProvider';
import { ThemeContext } from '../../../contexts/ThemeContext';
import { darkTheme, lightTheme } from '../../../config/themes';
import { createCompanyBrandTheme } from '../../../config/companyBrandingTheme';

let root: Root | undefined;
let container: HTMLDivElement | undefined;
afterEach(async () => {
  await act(async () => root?.unmount());
  container?.remove();
});

function ThemeProbe({ name }: { name: string }) {
  const themeClass = useThemeClassName();
  return <output data-testid={name} className={themeClass} />;
}

async function render(accent: string | null, dark = false) {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  const base = dark ? darkTheme : lightTheme;
  await act(async () => root!.render(
    <ThemeContext.Provider value={{
      savedTheme: dark ? 'Dark' : 'Light', currentTheme: dark ? 'Dark' : 'Light',
      themeStyles: base, isDarkMode: dark, setTheme: () => {},
    }}>
      <FluentProvider theme={base}>
        <ThemeProbe name="public" />
        <CompanyAccentProvider accentColor={accent} className="shell"
          style={{ '--diaglink-company-accent': accent ?? 'var(--colorBrandForeground1)' } as CSSProperties}>
          <ThemeProbe name="authenticated" />
          <Button appearance="primary">Save</Button>
          <Button appearance="primary" disabled>Disabled</Button>
          <Button appearance="primary" disabledFocusable>Focusable disabled</Button>
          <Button appearance="primary" data-company-accent-exempt icon={<DeleteRegular />}>Delete</Button>
          <Button appearance="subtle" icon={<SettingsRegular />}>Settings</Button>
          <Button appearance="subtle" data-company-accent-exempt icon={<DeleteRegular />}>Delete icon</Button>
          <Link href="#family">Link</Link>
          <TabList selectedValue="selected"><Tab value="selected">Selected</Tab></TabList>
          <Checkbox checked label="Check" />
          <Radio checked label="Radio" />
          <Switch checked label="Toggle" />
          <Input aria-label="Input" />
          <Spinner label="Loading" />
          <Badge color="informative">Info</Badge>
          <Dialog open modalType="non-modal">
            <DialogSurface><DialogBody>
              <DialogTitle>Portal</DialogTitle>
              <ThemeProbe name="portal" />
              <Button appearance="primary">Portal save</Button>
            </DialogBody></DialogSurface>
          </Dialog>
        </CompanyAccentProvider>
      </FluentProvider>
    </ThemeContext.Provider>,
  ));
  return (name: string) => {
    const computed = getComputedStyle(document.querySelector(`[data-testid="${name}"]`)!);
    return Object.fromEntries(Object.keys(base).map(key => [key, computed.getPropertyValue(`--${key}`).trim()]));
  };
}

const serialized = (theme: object) => Object.fromEntries(Object.entries(theme).map(([key, value]) => [key, String(value)]));

describe('authenticated scoped brand theme', () => {
  it.each([false, true])('covers Fluent families and real dialog portals without changing the public theme (dark=%s)', async dark => {
    const probe = await render('#9A5747', dark);
    const base = dark ? darkTheme : lightTheme;
    const expected = { ...base, ...createCompanyBrandTheme('#9A5747', dark) };
    expect(probe('public')).toEqual(serialized(base));
    expect(probe('authenticated')).toEqual(serialized(expected));
    expect(probe('portal')).toEqual(serialized(expected));
    const portal = document.querySelector('[data-testid="portal"]')!.closest('[data-portal-node]')!;
    expect(portal.classList.contains('shell')).toBe(false);
    expect(portal.classList.contains('fui-FluentProvider')).toBe(false);
    expect(getComputedStyle(portal).fontFamily).toBe('var(--fontFamilyBase)');
    expect(getComputedStyle(portal).lineHeight).toBe('var(--lineHeightBase300)');
    expect(getComputedStyle(portal).backgroundColor).toBe('rgba(0, 0, 0, 0)');
    for (const family of ['Button', 'Link', 'Tab', 'Checkbox', 'Radio', 'Switch', 'Input', 'Spinner', 'Badge']) {
      expect(document.querySelector(`.fui-${family}`)).not.toBeNull();
    }
    expect(container!.querySelector('[data-company-accent]')).not.toBeNull();
    const buttons = Array.from(document.querySelectorAll('button'));
    expect(buttons.find(button => button.textContent === 'Disabled')?.disabled).toBe(true);
    expect(buttons.find(button => button.textContent === 'Focusable disabled')?.getAttribute('aria-disabled')).toBe('true');
    expect(probe('authenticated').colorPaletteRedForeground1).toBe(base.colorPaletteRedForeground1);
    expect(probe('authenticated').colorStatusSuccessForeground1).toBe(base.colorStatusSuccessForeground1);
  });

  it.each([false, true])('uses the exact unchanged base theme, including portals, when accent is null (dark=%s)', async dark => {
    const probe = await render(null, dark);
    const base = dark ? darkTheme : lightTheme;
    expect(container!.querySelector('[data-company-accent]')).toBeNull();
    expect(probe('public')).toEqual(serialized(base));
    expect(probe('authenticated')).toEqual(serialized(base));
    expect(probe('portal')).toEqual(serialized(base));
  });
});
