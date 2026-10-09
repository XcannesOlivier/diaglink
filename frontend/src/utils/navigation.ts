import type { AppView } from '../types/navigation';
import type { DiagLinkRole } from '../types/currentUser';

export interface NavItem {
  view: AppView;
  label: string;
  badgeCount?: number;
}

/** Nav entries per role, in display order — mirrors the structure requested for DiagLink navigation. */
const NAV_ITEMS_BY_ROLE: Record<DiagLinkRole, NavItem[]> = {
  technician: [
    { view: 'chat', label: 'Chat / Assistance' },
    { view: 'machines', label: 'Machines' },
    { view: 'history', label: 'Historique' },
  ],
  company_admin: [
    { view: 'chat', label: 'Chat / Assistance' },
    { view: 'machines', label: 'Machines' },
    { view: 'history', label: 'Historique' },
    { view: 'users', label: 'Utilisateurs' },
    { view: 'company', label: 'Crédits et abonnement' },
    { view: 'personalization', label: 'Personnalisation' },
  ],
  diaglink_super_admin: [
    { view: 'chat', label: 'Chat / Assistance' },
    { view: 'machine-requests', label: 'Nouvelles demandes' },
    { view: 'companies', label: 'Entreprises' },
    { view: 'users', label: 'Utilisateurs' },
    { view: 'machines', label: 'Machines' },
    { view: 'diaglink-admin', label: 'Administration DiagLink' },
  ],
};

/** Human-readable role labels — never show the raw role string in the UI. */
export const ROLE_LABELS: Record<DiagLinkRole, string> = {
  technician: 'Technicien',
  company_admin: 'Administrateur entreprise',
  diaglink_super_admin: 'Super administrateur DiagLink',
};

export function getNavItemsForRole(role: DiagLinkRole | undefined, pendingMachineRequestCount = 0): NavItem[] {
  if (!role) return [];
  const items = NAV_ITEMS_BY_ROLE[role];
  if (role === 'diaglink_super_admin') {
    return items.map(item => item.view === 'machine-requests' && pendingMachineRequestCount > 0
      ? { ...item, badgeCount: pendingMachineRequestCount }
      : item);
  }
  return role === 'technician' || role === 'company_admin'
    ? items.filter(item => item.view !== 'history' && item.view !== 'personalization')
    : items;
}

export function getAllowedViews(role: DiagLinkRole | undefined): AppView[] {
  // Visibility in the header must not change access to existing views.
  return role ? NAV_ITEMS_BY_ROLE[role].map(item => item.view) : [];
}

/** Falls back to 'chat' when the requested view isn't part of the role's allowed navigation. */
export function resolveView(requestedView: AppView, role: DiagLinkRole | undefined): AppView {
  return getAllowedViews(role).includes(requestedView) ? requestedView : 'chat';
}
