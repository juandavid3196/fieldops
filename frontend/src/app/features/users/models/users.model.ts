/** Canonical roles (BR-06), in display order. */
export const ROLE_CODES = [
  'owner',
  'operations_manager',
  'dispatcher',
  'technician',
  'accounting',
  'viewer',
] as const;
export type RoleCode = (typeof ROLE_CODES)[number];

export type UserKind = 'member' | 'invitation';
/** `GET /users?status=` values (BR-03). */
export type UserStatusFilter = 'active' | 'suspended' | 'pending_invitation';
export type UserSort = 'name' | '-name';
export const PAGE_SIZE = 10;

export interface BranchRef {
  readonly id: string;
  readonly name: string;
}

export interface TeamProfileRef {
  readonly applicable: boolean;
  readonly name: string | null;
}

/** Row of `GET /users` and the body of every row-returning mutation (BR-04). */
export interface UserRow {
  readonly id: string;
  readonly kind: UserKind;
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string;
  readonly roleCode: string;
  readonly roleName: string;
  readonly isAllBranches: boolean;
  readonly branches: readonly BranchRef[];
  readonly teamProfile: TeamProfileRef;
  readonly status: string;
  readonly isExpired: boolean;
  readonly lastActiveAt: string | null;
  readonly isCurrentUser: boolean;
  readonly isLastOwner: boolean;
}

export interface UserListResponse {
  readonly items: readonly UserRow[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

export interface UserListQuery {
  readonly search: string;
  readonly roleCode: string | null;
  readonly branchId: string | null;
  readonly status: UserStatusFilter | null;
  readonly sort: UserSort;
  readonly page: number;
}

export interface UserSummary {
  readonly activeUsers: number;
  readonly pendingInvitations: number;
  readonly suspendedUsers: number;
  readonly owners: number;
}

export interface AccessRequest {
  readonly roleCode: string;
  readonly isAllBranches: boolean;
  readonly branchIds: readonly string[];
}

/** Body of `POST /users/invitations` (BR-08). */
export interface InviteRequest extends AccessRequest {
  readonly email: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly linkTeamProfile: boolean;
  readonly expiresInDays: number;
}

export interface MatrixRole {
  readonly code: string;
  readonly name: string;
  readonly summary: string;
  readonly forcesAllBranches: boolean;
  readonly hasTeamProfile: boolean;
}

export interface MatrixLevel {
  readonly level: string;
  readonly label: string;
}

export interface MatrixModule {
  readonly key: string;
  readonly name: string;
  readonly levels: Readonly<Record<string, MatrixLevel>>;
}

export interface PermissionMatrix {
  readonly roles: readonly MatrixRole[];
  readonly modules: readonly MatrixModule[];
}

/** Drawer field keys, in form order. */
export const INVITE_FIELD_KEYS = [
  'email',
  'firstName',
  'lastName',
  'roleCode',
  'branchIds',
  'expiresInDays',
] as const;
export type InviteFieldKey = (typeof INVITE_FIELD_KEYS)[number];
export type InviteFieldErrors = Partial<Record<InviteFieldKey, string>>;
