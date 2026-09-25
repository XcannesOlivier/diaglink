import { describe, it, expect } from 'vitest';
import { getAllowedViews, getNavItemsForRole, resolveView, ROLE_LABELS } from '../navigation';

describe('navigation role gating', () => {
  it('technician does not have access to users/company/diaglink-admin views', () => {
    const allowed = getAllowedViews('technician');
    expect(allowed).toEqual(['chat', 'machines', 'history']);
    expect(allowed).not.toContain('users');
    expect(allowed).not.toContain('company');
    expect(allowed).not.toContain('diaglink-admin');
  });

  it('company_admin has access to users and company but not diaglink-admin/companies', () => {
    const allowed = getAllowedViews('company_admin');
    expect(allowed).toContain('users');
    expect(allowed).toContain('company');
    expect(allowed).not.toContain('diaglink-admin');
    expect(allowed).not.toContain('companies');
  });

  it('diaglink_super_admin has access to companies, users, machines and diaglink-admin', () => {
    const allowed = getAllowedViews('diaglink_super_admin');
    expect(allowed).toContain('companies');
    expect(allowed).toContain('users');
    expect(allowed).toContain('machines');
    expect(allowed).toContain('machine-requests');
    expect(allowed).toContain('diaglink-admin');
  });

  it('resolveView falls back to chat when the requested view is not allowed for the role', () => {
    expect(resolveView('users', 'technician')).toBe('chat');
    expect(resolveView('diaglink-admin', 'company_admin')).toBe('chat');
    expect(resolveView('companies', 'company_admin')).toBe('chat');
    expect(resolveView('machine-requests', 'company_admin')).toBe('chat');
  });

  it('resolveView keeps the requested view when it is allowed for the role', () => {
    expect(resolveView('users', 'company_admin')).toBe('users');
    expect(resolveView('diaglink-admin', 'diaglink_super_admin')).toBe('diaglink-admin');
    expect(resolveView('machine-requests', 'diaglink_super_admin')).toBe('machine-requests');
  });

  it('resolveView falls back to chat when there is no role yet', () => {
    expect(resolveView('machines', undefined)).toBe('chat');
  });

  it('exposes a human-readable label for every role', () => {
    expect(ROLE_LABELS.technician).toBe('Technicien');
    expect(ROLE_LABELS.company_admin).toBe('Administrateur entreprise');
    expect(ROLE_LABELS.diaglink_super_admin).toBe('Super administrateur DiagLink');
  });

  it('shows the real pending request count only when it is positive', () => {
    expect(getNavItemsForRole('diaglink_super_admin', 3).find(item => item.view === 'machine-requests')?.badgeCount).toBe(3);
    expect(getNavItemsForRole('diaglink_super_admin', 0).find(item => item.view === 'machine-requests')?.badgeCount).toBeUndefined();
    expect(getNavItemsForRole('company_admin', 3).some(item => item.view === 'machine-requests')).toBe(false);
  });
});
