import { AccessRequest, BranchRef, MatrixRole, ROLE_CODES, UserRow } from '../models/users.model';

export const LAST_OWNER_TOOLTIP = "The last Owner can't be suspended or downgraded.";
export const SELF_SUSPEND_TOOLTIP = "You can't suspend your own access.";
export const NOT_PENDING_MESSAGE = 'This invitation is no longer pending.';
export const USER_UNAVAILABLE_MESSAGE = 'This user is no longer available.';
export const DELIVERY_FAILED_MESSAGE = "We couldn't send the invitation. Try again.";

/**
 * Structural role facts used only until (or without) `GET /permission-matrix`: role names and
 * flags are canonical (BR-06/BR-08). Summaries always come from the API.
 */
export const FALLBACK_ROLES: readonly MatrixRole[] = [
  { code: 'owner', name: 'Owner', summary: '', forcesAllBranches: true, hasTeamProfile: false },
  {
    code: 'operations_manager',
    name: 'Operations Manager',
    summary: '',
    forcesAllBranches: true,
    hasTeamProfile: true,
  },
  {
    code: 'dispatcher',
    name: 'Dispatcher',
    summary: '',
    forcesAllBranches: false,
    hasTeamProfile: true,
  },
  {
    code: 'technician',
    name: 'Technician',
    summary: '',
    forcesAllBranches: false,
    hasTeamProfile: true,
  },
  {
    code: 'accounting',
    name: 'Accounting',
    summary: '',
    forcesAllBranches: false,
    hasTeamProfile: false,
  },
  { code: 'viewer', name: 'Viewer', summary: '', forcesAllBranches: false, hasTeamProfile: false },
];

/** Position in the BR-06 order (Owner highest); unknown roles rank last. */
export function roleRank(code: string): number {
  const index = (ROLE_CODES as readonly string[]).indexOf(code);
  return index === -1 ? ROLE_CODES.length : index;
}

/** BR-12: access is reduced when the role moves down, a branch is removed or All branches is turned off. */
export function isAccessReduced(row: UserRow, next: AccessRequest): boolean {
  if (roleRank(next.roleCode) > roleRank(row.roleCode)) {
    return true;
  }
  if (row.isAllBranches) {
    return !next.isAllBranches;
  }
  if (next.isAllBranches) {
    return false;
  }
  const kept = new Set(next.branchIds);
  return row.branches.some((branch) => !kept.has(branch.id));
}

function branchText(all: boolean, branches: readonly BranchRef[]): string {
  if (all) {
    return 'All branches';
  }
  return branches.length === 0 ? 'None' : branches.map((branch) => branch.name).join(', ');
}

/** BR-12 confirmation copy: "Change access for {name}? Role: {old} → {new}. Branches: {old} → {new}." */
export function changeAccessMessage(
  row: UserRow,
  name: string,
  newRoleName: string,
  next: AccessRequest,
  branchOptions: readonly BranchRef[],
): string {
  const selected = branchOptions.filter((branch) => next.branchIds.includes(branch.id));
  return (
    `Change access for ${name}? Role: ${row.roleName} → ${newRoleName}. ` +
    `Branches: ${branchText(row.isAllBranches, row.branches)} → ${branchText(next.isAllBranches, selected)}.`
  );
}

/** Branch access column text. */
export function branchAccessText(row: UserRow): string {
  return branchText(row.isAllBranches, row.branches);
}
