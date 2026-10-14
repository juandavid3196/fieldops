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
