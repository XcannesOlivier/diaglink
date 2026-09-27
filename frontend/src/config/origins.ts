export const PUBLIC_ORIGIN = 'https://diaglink.com';
export const APP_ORIGIN = 'https://app.diaglink.com';

export const APP_LOGIN_URL = `${APP_ORIGIN}/login`;
export const APP_INSTALL_URL = `${APP_LOGIN_URL}?install=1`;

export function getEntraRedirectOrigin(isDevelopment: boolean, currentOrigin: string) {
  return isDevelopment ? currentOrigin : APP_ORIGIN;
}
