/** UX-only navigation destinations — security is enforced server-side, this only drives what's rendered. */
export type AppView =
  | 'chat'
  | 'machines'
  | 'history'
  | 'users'
  | 'company'
  | 'companies'
  | 'diaglink-admin';
