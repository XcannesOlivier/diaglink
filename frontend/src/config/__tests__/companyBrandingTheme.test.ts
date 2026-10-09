import { describe, expect, it } from 'vitest';
import { createCompanyBrandTheme, isCompanyBrandToken } from '../companyBrandingTheme';
import { darkTheme, lightTheme } from '../themes';

const presets = ['#356A9A', '#39756B', '#77639B', '#986B36', '#9A5747', '#32707A'];
function luminance(color: string) {
  const channels = [1, 3, 5].map(offset => {
    const c = Number.parseInt(color.slice(offset, offset + 2), 16) / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
}
function contrast(a: string, b: string) {
  const x = luminance(a), y = luminance(b);
  return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05);
}

describe('company brand theme derivation', () => {
  it.each([false, true])('does not override any token without a configured accent (dark=%s)', dark => {
    expect(createCompanyBrandTheme(null, dark)).toBeUndefined();
  });

  for (const dark of [false, true]) {
    it.each(presets)('keeps %s as principal fill with readable text and coherent interactions', accent => {
      const brand = createCompanyBrandTheme(accent, dark)!;
      const base = dark ? darkTheme : lightTheme;
      const theme = { ...base, ...brand };
      expect(theme.colorBrandBackground).toBe(accent);
      for (const key of ['colorBrandBackground', 'colorBrandBackgroundHover', 'colorBrandBackgroundPressed',
        'colorCompoundBrandBackground', 'colorCompoundBrandBackgroundHover', 'colorCompoundBrandBackgroundPressed'] as const) {
        expect(contrast(theme[key], theme.colorNeutralForegroundOnBrand)).toBeGreaterThanOrEqual(4.5);
      }
      for (const key of ['colorBrandForeground1', 'colorBrandForegroundLink', 'colorBrandForegroundLinkHover',
        'colorBrandForegroundLinkPressed', 'colorNeutralForeground2BrandHover', 'colorCompoundBrandForeground1'] as const) {
        expect(contrast(theme[key], base.colorNeutralBackground1))
          .toBeGreaterThanOrEqual(4.5);
      }
      for (const [key, value] of Object.entries(theme)) {
        if (!isCompanyBrandToken(key)) expect(value).toBe(base[key as keyof typeof base]);
      }
    });
  }

  it.each([false, true])('eliminates all chromatic DiagLink brand tokens, including neutral-named selected states (dark=%s)', dark => {
    const base = dark ? darkTheme : lightTheme;
    for (const accent of ['#9A5747', '#32707A']) {
      const theme = { ...base, ...createCompanyBrandTheme(accent, dark) };
      for (const [key, value] of Object.entries(base)) {
        if (isCompanyBrandToken(key) && typeof value === 'string' && /^#[0-9a-f]{6}$/i.test(value) &&
          value.slice(1, 3) !== value.slice(3, 5)) {
          expect(theme[key], key).not.toBe(value);
        }
      }
    }
  });

  it.each(['#ffffff', '#000000', '#aAbBcC'])('supports saved out-of-palette colors with readable on-brand labels: %s', accent => {
    for (const dark of [false, true]) {
      const theme = { ...(dark ? darkTheme : lightTheme), ...createCompanyBrandTheme(accent, dark) };
      expect(theme.colorBrandBackground).toBe(accent);
      for (const fill of [theme.colorBrandBackground, theme.colorBrandBackgroundHover, theme.colorBrandBackgroundPressed]) {
        expect(contrast(fill, theme.colorNeutralForegroundOnBrand)).toBeGreaterThanOrEqual(4.5);
      }
      expect(contrast(theme.colorBrandForegroundOnLight, lightTheme.colorNeutralBackground1)).toBeGreaterThanOrEqual(4.5);
      expect(contrast(theme.colorBrandForegroundInverted, dark ? lightTheme.colorNeutralBackground1 : darkTheme.colorNeutralBackground1))
        .toBeGreaterThanOrEqual(4.5);
      expect(contrast(theme.colorBrandForeground1, theme.colorNeutralBackground1)).toBeGreaterThanOrEqual(4.5);
    }
  });

  it('fails explicitly on invalid metadata instead of silently substituting a brand', () => {
    expect(() => createCompanyBrandTheme('invalid', false)).toThrow('expected #RRGGBB');
  });
});
