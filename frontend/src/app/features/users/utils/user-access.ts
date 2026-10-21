import { AccessRequest, BranchRef, MatrixRole, ROLE_CODES, UserRow } from '../models/users.model';

import { localizedList, t } from '../../../core/i18n/translate';
export const LAST_OWNER_TOOLTIP = (): string => t('users.userAccess.lastOwnerTooltip');
export const SELF_SUSPEND_TOOLTIP = (): string => t('users.userAccess.selfSuspendTooltip');
export const NOT_PENDING_MESSAGE = (): string => t('users.userAccess.notPendingMessage');
export const USER_UNAVAILABLE_MESSAGE = (): string => t('users.userAccess.userUnavailableMessage');
export const DELIVERY_FAILED_MESSAGE = (): string => t('users.userAccess.deliveryFailedMessage');

/**
 * Structural role facts used only until (or without) `GET /permission-matrix`: role names and
 * flags are canonical (BR-06/BR-08). Summaries always come from the API.
 */
export const FALLBACK_ROLES: readonly MatrixRole[] = localizedList(() => [
  {
    code: 'owner',
    name: t('users.userAccess.owner'),
    summary: '',
    forcesAllBranches: true,
    hasTeamProfile: false,
  },
  {
    code: 'operations_manager',
    name: t('users.userAccess.operationsManager'),
    summary: '',
    forcesAllBranches: true,
    hasTeamProfile: true,
  },
  {
    code: 'dispatcher',
    name: t('users.userAccess.dispatcher'),
    summary: '',
    forcesAllBranches: false,
    hasTeamProfile: true,
  },
  {
    code: 'technician',
    name: t('users.userAccess.technician'),
    summary: '',
    forcesAllBranches: false,
    hasTeamProfile: true,
  },
  {
    code: 'accounting',
    name: t('users.userAccess.accounting'),
    summary: '',
    forcesAllBranches: false,
    hasTeamProfile: false,
  },
  {
    code: 'viewer',
    name: t('users.userAccess.viewer'),
    summary: '',
    forcesAllBranches: false,
    hasTeamProfile: false,
  },
]);

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
    return t('users.userAccess.allBranches');
  }
  return branches.length === 0
    ? t('users.userAccess.none')
    : branches.map((branch) => branch.name).join(', ');
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
    t('users.userAccess.changeAccessForRole', {
      name: name,
      roleName: row.roleName,
      newRoleName: newRoleName,
    }) +
    t('users.userAccess.branches', {
      value: branchText(row.isAllBranches, row.branches),
      value2: branchText(next.isAllBranches, selected),
    })
  );
}

/** Branch access column text. */
export function branchAccessText(row: UserRow): string {
  return branchText(row.isAllBranches, row.branches);
}
