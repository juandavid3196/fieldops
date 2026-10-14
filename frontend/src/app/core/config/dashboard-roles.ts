/**
 * operations-dashboard BR-01: roles with Manage access to the dashboard (UX only; the backend
 * decides with `403`).
 */
export const DASHBOARD_ROLE_CODES: readonly string[] = [
  'owner',
  'operations_manager',
  'dispatcher',
];

export function canSeeDashboard(roleCode: string | null | undefined): boolean {
  return DASHBOARD_ROLE_CODES.includes(roleCode ?? '');
}

/** business-overview BR-01: only the Owner sees the Business view (UX only; the backend decides). */
export function canSeeBusinessOverview(roleCode: string | null | undefined): boolean {
  return roleCode === 'owner';
}
