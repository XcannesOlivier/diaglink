import { createDarkTheme, createLightTheme, type BrandVariants, type Theme } from '@fluentui/react-components';
import { darkTheme, lightTheme } from './themes';

function mix(color: string, target: number, amount: number): string {
  const channels = [1, 3, 5].map(offset => {
    const channel = Number.parseInt(color.slice(offset, offset + 2), 16);
    return Math.round(channel + (target - channel) * amount).toString(16).padStart(2, '0');
  });
  return `#${channels.join('')}`;
}

type CompanyBrandToken = Extract<keyof Theme,
  `colorBrand${string}` | `colorCompoundBrand${string}` | `colorNeutralForeground${2 | 3}Brand${string}` |
  'colorNeutralStrokeAccessibleSelected' | `shadow${number}Brand` | 'colorNeutralForegroundOnBrand'>;

export function isCompanyBrandToken(key: string): key is CompanyBrandToken {
  return key.startsWith('colorBrand') || key.startsWith('colorCompoundBrand') ||
    /^colorNeutralForeground[23]Brand/.test(key) || key === 'colorNeutralStrokeAccessibleSelected' ||
    /^shadow\d+Brand$/.test(key) || key === 'colorNeutralForegroundOnBrand';
}

function luminance(color: string): number {
  const channels = [1, 3, 5].map(offset => {
    const channel = Number.parseInt(color.slice(offset, offset + 2), 16) / 255;
    return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });
  return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
}

function readableForeground(color: string, background: string, dark: boolean): string {
  const backgroundLuminance = luminance(background);
  for (let step = 0; step <= 20; step++) {
    const candidate = mix(color, dark ? 255 : 0, step / 20);
    const foregroundLuminance = luminance(candidate);
    const contrast = (Math.max(foregroundLuminance, backgroundLuminance) + 0.05) /
      (Math.min(foregroundLuminance, backgroundLuminance) + 0.05);
    if (contrast >= 4.5) return candidate;
  }
  throw new Error('Unable to derive a readable company brand foreground.');
}

/** Fluent assigns different ramp steps to fills, foregrounds and interactions in each mode. */
export function createCompanyBrandTheme(accent: string | null, isDarkMode: boolean): Partial<Theme> | undefined {
  if (accent === null) return undefined;
  if (!/^#[0-9a-f]{6}$/i.test(accent)) throw new Error('Invalid company accent: expected #RRGGBB.');

  const ramp: BrandVariants = {
    10: mix(accent, 0, 0.92), 20: mix(accent, 0, 0.84),
    30: mix(accent, 0, 0.65), 40: mix(accent, 0, 0.45),
    50: mix(accent, 0, 0.3), 60: mix(accent, 0, 0.18),
    70: mix(accent, 0, 0.08), 80: accent,
    90: mix(accent, 255, 0.22), 100: mix(accent, 255, 0.4),
    110: mix(accent, 255, 0.5), 120: mix(accent, 255, 0.6),
    130: mix(accent, 255, 0.7), 140: mix(accent, 255, 0.8),
    150: mix(accent, 255, 0.88), 160: mix(accent, 255, 0.94),
  };
  const generated = isDarkMode ? createDarkTheme(ramp) : createLightTheme(ramp);
  const brand: Partial<Pick<Theme, CompanyBrandToken>> = {};
  for (const [key, value] of Object.entries(generated)) {
    if (isCompanyBrandToken(key) && typeof value === 'string') {
      const foreground = key.includes('Foreground') && key !== 'colorNeutralForegroundOnBrand';
      const darkSurface = key.includes('Inverted') ? !isDarkMode : !key.includes('OnLight') && isDarkMode;
      brand[key] = foreground ? readableForeground(value,
        darkSurface ? darkTheme.colorNeutralBackground1 : lightTheme.colorNeutralBackground1, darkSurface) : value;
    }
  }

  // Dark-mode foregrounds are lighter, but action fills keep the saved accent.
  const whiteContrast = 1.05 / (luminance(accent) + 0.05);
  const hover = whiteContrast >= 4.5 ? ramp[70] : mix(accent, 255, 0.08);
  const pressed = whiteContrast >= 4.5 ? ramp[60] : mix(accent, 255, 0.18);
  brand.colorBrandBackground = accent;
  brand.colorBrandBackgroundHover = hover;
  brand.colorBrandBackgroundPressed = pressed;
  brand.colorBrandBackgroundSelected = hover;
  brand.colorCompoundBrandBackground = accent;
  brand.colorCompoundBrandBackgroundHover = hover;
  brand.colorCompoundBrandBackgroundPressed = pressed;
  brand.colorNeutralForegroundOnBrand = whiteContrast >= 4.5 ? '#ffffff' : '#000000';
  return brand;
}
