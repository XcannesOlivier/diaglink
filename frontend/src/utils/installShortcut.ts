export const CHAT_ROUTE = '/app';
export const pendingInstallPlatformKey = 'diaglink.pendingInstallPlatform';

export type ShortcutPlatform = 'windows' | 'ios' | 'android' | 'other';
export type ShortcutBrowser = 'edge' | 'chrome' | 'other';

export function detectShortcutPlatform(
  navigatorLike: Pick<Navigator, 'userAgent' | 'platform' | 'maxTouchPoints'> = window.navigator,
): ShortcutPlatform {
  const userAgent = navigatorLike.userAgent.toLowerCase();
  const platform = navigatorLike.platform.toLowerCase();
  if (userAgent.includes('android')) return 'android';
  if (/iphone|ipad|ipod/.test(userAgent) || (platform === 'macintel' && navigatorLike.maxTouchPoints > 1)) return 'ios';
  if (userAgent.includes('windows') || platform.startsWith('win')) return 'windows';
  return 'other';
}

export function detectShortcutBrowser(userAgent = window.navigator.userAgent): ShortcutBrowser {
  const normalized = userAgent.toLowerCase();
  if (normalized.includes('edg/')) return 'edge';
  if (normalized.includes('chrome/') || normalized.includes('crios/')) return 'chrome';
  return 'other';
}
