import type { CurrentUser } from '../types/currentUser';

/** Every role helper accepts `CurrentUser | null` so callers can pass state.auth.currentUser as-is. */
export const isTechnician = (user: CurrentUser | null): boolean => user?.role === 'technician';
export const isCompanyAdmin = (user: CurrentUser | null): boolean => user?.role === 'company_admin';
export const isSuperAdmin = (user: CurrentUser | null): boolean => user?.role === 'diaglink_super_admin';

/** True for company_admin and diaglink_super_admin — mirrors the backend's CompanyAdminOrAbove policy. */
export const canManageCompany = (user: CurrentUser | null): boolean =>
  isCompanyAdmin(user) || isSuperAdmin(user);

/** True only for diaglink_super_admin — mirrors the backend's SuperAdminOnly policy. */
export const canManageDiagLink = (user: CurrentUser | null): boolean => isSuperAdmin(user);
