import { describe, expect, it } from 'vitest';
import { detectShortcutBrowser, detectShortcutPlatform } from '../installShortcut';

function navigatorFor(userAgent: string, platform: string, maxTouchPoints = 0) {
  return { userAgent, platform, maxTouchPoints } as Pick<Navigator, 'userAgent' | 'platform' | 'maxTouchPoints'>;
}

describe('installShortcut', () => {
  it.each([
    ['windows', navigatorFor('Mozilla/5.0 (Windows NT 10.0)', 'Win32')],
    ['android', navigatorFor('Mozilla/5.0 (Linux; Android 15)', 'Linux armv8l', 5)],
    ['ios', navigatorFor('Mozilla/5.0 (iPhone)', 'iPhone', 5)],
    ['ios', navigatorFor('Mozilla/5.0 (Macintosh)', 'MacIntel', 5)],
    ['macos', navigatorFor('Mozilla/5.0 (Macintosh; Intel Mac OS X)', 'MacIntel')],
    ['other', navigatorFor('Mozilla/5.0 (X11; Linux x86_64)', 'Linux x86_64')],
  ] as const)('detects %s', (expected, navigatorLike) => {
    expect(detectShortcutPlatform(navigatorLike)).toBe(expected);
  });

  it.each([
    ['edge', 'Mozilla/5.0 Chrome/140.0 Safari/537.36 Edg/140.0'],
    ['edge', 'Mozilla/5.0 (Linux; Android 15) Chrome/140.0 EdgA/140.0'],
    ['edge', 'Mozilla/5.0 (iPhone) EdgiOS/140.0'],
    ['samsung', 'Mozilla/5.0 (Linux; Android 15) SamsungBrowser/28.0 Chrome/140.0'],
    ['chrome', 'Mozilla/5.0 Chrome/140.0 Safari/537.36'],
    ['chrome', 'Mozilla/5.0 (iPhone) CriOS/140.0 Mobile Safari/604.1'],
    ['firefox', 'Mozilla/5.0 Firefox/142.0'],
    ['firefox', 'Mozilla/5.0 (iPhone) FxiOS/142.0 Mobile Safari/605.1'],
    ['safari', 'Mozilla/5.0 (Macintosh) Version/18.0 Safari/605.1.15'],
    ['other', 'CustomBrowser/1.0'],
  ] as const)('detects the %s browser', (expected, userAgent) => {
    expect(detectShortcutBrowser(userAgent)).toBe(expected);
  });
});
