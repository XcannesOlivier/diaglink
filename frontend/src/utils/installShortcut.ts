export type ShortcutPlatform = 'windows' | 'macos' | 'ios' | 'android' | 'other';
export type ShortcutBrowser = 'edge' | 'chrome' | 'safari' | 'firefox' | 'samsung' | 'other';

export function detectShortcutPlatform(
  navigatorLike: Pick<Navigator, 'userAgent' | 'platform' | 'maxTouchPoints'> = window.navigator,
): ShortcutPlatform {
  const userAgent = navigatorLike.userAgent.toLowerCase();
  const platform = navigatorLike.platform.toLowerCase();
  if (userAgent.includes('android')) return 'android';
  if (/iphone|ipad|ipod/.test(userAgent) || (platform === 'macintel' && navigatorLike.maxTouchPoints > 1)) return 'ios';
  if (userAgent.includes('windows') || platform.startsWith('win')) return 'windows';
  if (userAgent.includes('macintosh') || platform.startsWith('mac')) return 'macos';
  return 'other';
}

export function detectShortcutBrowser(userAgent = window.navigator.userAgent): ShortcutBrowser {
  const normalized = userAgent.toLowerCase();
  if (/edg(?:a|ios)?\//.test(normalized)) return 'edge';
  if (normalized.includes('samsungbrowser/')) return 'samsung';
  if (normalized.includes('chrome/') || normalized.includes('crios/')) return 'chrome';
  if (normalized.includes('firefox/') || normalized.includes('fxios/')) return 'firefox';
  if (normalized.includes('safari/')) return 'safari';
  return 'other';
}
