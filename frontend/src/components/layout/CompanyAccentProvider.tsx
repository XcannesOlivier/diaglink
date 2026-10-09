import { useContext, useMemo, useRef, type CSSProperties, type ReactNode } from 'react';
import {
  makeStyles, mergeClasses, shorthands, tokens, typographyStyles, type ButtonState, type FluentProviderProps,
  useFluentProvider_unstable, useFluentProviderStyles_unstable,
  useFluentProviderContextValues_unstable, renderFluentProvider_unstable,
} from '@fluentui/react-components';
import { useThemeContext } from '../../contexts/ThemeContext';
import { CompanyAccentContext } from '../../contexts/CompanyAccentContext';
import { createCompanyBrandTheme } from '../../config/companyBrandingTheme';

const useSemanticStyles = makeStyles({
  icon: {
    '@media (forced-colors: none)': {
      ':hover .fui-Button__icon': { color: 'inherit' },
      ':active .fui-Button__icon': { color: 'inherit' },
    },
  },
  destructive: {
    '@media (forced-colors: none)': {
      backgroundColor: tokens.colorPaletteRedBackground1,
      ...shorthands.borderColor(tokens.colorPaletteRedBorder1),
      color: tokens.colorPaletteRedForeground1,
      ':hover': { backgroundColor: tokens.colorPaletteRedBackground2, color: tokens.colorPaletteRedForeground1 },
      ':active': { backgroundColor: tokens.colorPaletteRedBackground1, color: tokens.colorPaletteRedForeground1 },
    },
  },
});

function isButtonState(state: unknown): state is ButtonState {
  return typeof state === 'object' && state !== null && 'root' in state &&
    typeof state.root === 'object' && state.root !== null && 'appearance' in state;
}

function useCompanyButtonStyles(state: unknown) {
  const accent = useContext(CompanyAccentContext);
  const semanticStyles = useSemanticStyles();
  if (!isButtonState(state) || !accent || state.disabled || state.disabledFocusable) return;
  if ('data-company-accent-exempt' in state.root && state.root['data-company-accent-exempt']) {
    state.root.className = mergeClasses(state.root.className, semanticStyles.icon,
      state.appearance === 'primary' ? semanticStyles.destructive : undefined);
  }
}

const customStyleHooks = { useButtonStyles_unstable: useCompanyButtonStyles };

const usePortalStyles = makeStyles({
  typography: {
    ...typographyStyles.body1,
    color: tokens.colorNeutralForeground1,
    textAlign: 'start',
  },
});

function ScopedFluentProvider(props: FluentProviderProps) {
  const ref = useRef<HTMLDivElement>(null);
  const state = useFluentProvider_unstable({ ...props, applyStylesToPortals: false }, ref);
  useFluentProviderStyles_unstable(state);
  const contextValues = useFluentProviderContextValues_unstable(state);
  const portalStyles = usePortalStyles();
  // Portals inherit tokens and typography, never the shell's layout or opaque background.
  contextValues.themeClassName = mergeClasses(state.themeClassName, portalStyles.typography);
  return renderFluentProvider_unstable(state, contextValues);
}

interface CompanyAccentProviderProps {
  accentColor: string | null;
  className: string;
  style: CSSProperties;
  children: ReactNode;
}

type CompanyStatusStyles = CSSProperties & {
  '--diaglink-status-info-foreground'?: string;
  '--diaglink-status-info-background'?: string;
};

export function CompanyAccentProvider({ accentColor, className, style, children }: CompanyAccentProviderProps) {
  const { isDarkMode, themeStyles } = useThemeContext();
  const theme = useMemo(() => createCompanyBrandTheme(accentColor, isDarkMode), [accentColor, isDarkMode]);
  const scopedStyle: CompanyStatusStyles = accentColor ? {
    ...style,
    '--diaglink-status-info-foreground': themeStyles.colorBrandForeground1,
    '--diaglink-status-info-background': themeStyles.colorBrandBackground2,
  } : style;
  return (
    <CompanyAccentContext.Provider value={accentColor}>
      <ScopedFluentProvider className={className} style={scopedStyle} data-company-accent={accentColor ? '' : undefined}
        theme={theme} customStyleHooks_unstable={customStyleHooks}>
        {children}
      </ScopedFluentProvider>
    </CompanyAccentContext.Provider>
  );
}
