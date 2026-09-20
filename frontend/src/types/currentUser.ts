export type DiagLinkRole = 'technician' | 'company_admin' | 'diaglink_super_admin';

/**
 * The authenticated user's identity as resolved server-side from dbo.Users (GET /api/auth/me).
 * Never construct this from anything the client already knows — role/companyId are only
 * trustworthy when they come from that endpoint's response.
 */
export interface CurrentUser {
  userId: string;
  companyId: string;
  role: DiagLinkRole;
  email?: string;
}
