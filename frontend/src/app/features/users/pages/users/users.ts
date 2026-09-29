import { Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService, MenuItem, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { Menu } from 'primeng/menu';
import { Message } from 'primeng/message';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import {
  Observable,
  Subject,
  catchError,
  debounceTime,
  filter,
  map,
  of,
  switchMap,
  tap,
} from 'rxjs';

import { comingSoonPath } from '../../../../core/config/coming-soon-modules';
import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { BranchesService } from '../../../organizations/services/branches.service';
import { AdministrationNav } from '../../../organizations/components/administration-nav/administration-nav';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { PermissionMatrix } from '../../components/permission-matrix/permission-matrix';
import { UserDrawer, UserDrawerMode } from '../../components/user-drawer/user-drawer';
import { UserFilters } from '../../components/user-filters/user-filters';
import { UserMetrics } from '../../components/user-metrics/user-metrics';
import { UserTable } from '../../components/user-table/user-table';
import {
  BranchRef,
  PAGE_SIZE,
  PermissionMatrix as PermissionMatrixData,
  UserListQuery,
  UserListResponse,
  UserRow,
  UserSort,
  UserStatusFilter,
  UserSummary,
} from '../../models/users.model';
import { PermissionMatrixService } from '../../services/permission-matrix.service';
import { UsersService } from '../../services/users.service';
import {
  DELIVERY_FAILED_MESSAGE,
  FALLBACK_ROLES,
  LAST_OWNER_TOOLTIP,
  NOT_PENDING_MESSAGE,
  SELF_SUSPEND_TOOLTIP,
  USER_UNAVAILABLE_MESSAGE,
} from '../../utils/user-access';
import { displayStatus, fullName } from '../../utils/user-format';

export const FORBIDDEN_MESSAGE = "You don't have access to users and permissions.";
export const READ_ONLY_MESSAGE =
  'You have view-only access. Inviting users and editing access requires the Owner role.';
export const DESCRIPTION_MESSAGE =
  'Manage sign-in access, predefined roles, and branch visibility.';
export const LIST_ERROR_MESSAGE = "We couldn't load users. Check your connection and try again.";
export const NOTE_MESSAGE =
  'User access controls sign-in and permissions. Skills, workload, and availability are managed in';
export const EMPTY_MESSAGE = 'No users match these filters.';
const SEARCH_DEBOUNCE_MS = 300;
const SKELETON_ROWS = [0, 1, 2, 3, 4, 5];

type ListResult =
  | { readonly ok: true; readonly response: UserListResponse }
  | { readonly ok: false; readonly error: ApiError | null };

/**
 * Users & permissions page (`/admin/users`): metrics, filterable server-paged table, permission
 * matrix, invite/edit drawer and row actions. Single owner of the page state; Owner manages,
 * Viewer reads, any other role sees the forbidden state (BR-01, BR-14).
 */
@Component({
  selector: 'app-users',
  imports: [
    RouterLink,
    ButtonDirective,
    ConfirmDialog,
    Menu,
    Message,
    Paginator,
    Skeleton,
    Toast,
    AdministrationNav,
    PermissionMatrix,
    UserDrawer,
    UserFilters,
    UserMetrics,
    UserTable,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './users.html',
  styleUrl: './users.scss',
})
export class Users {
  private readonly usersService = inject(UsersService);
  private readonly matrixService = inject(PermissionMatrixService);
  private readonly branchesService = inject(BranchesService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly drawer = viewChild(UserDrawer);
  private readonly menu = viewChild.required(Menu);

  private readonly listRequests = new Subject<UserListQuery>();
  private readonly searchInput = new Subject<string>();

  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly readOnlyMessage = READ_ONLY_MESSAGE;
  readonly descriptionMessage = DESCRIPTION_MESSAGE;
  readonly listErrorMessage = LIST_ERROR_MESSAGE;
  readonly noteMessage = NOTE_MESSAGE;
  readonly emptyMessage = EMPTY_MESSAGE;
  readonly teamPath = comingSoonPath('team');
  readonly pageSize = PAGE_SIZE;
  readonly skeletonRows = SKELETON_ROWS;

  private readonly roleCodeOfSession = computed(
    () => this.sessionService.session()?.role.code ?? '',
  );
  readonly canManage = computed(() => this.roleCodeOfSession() === 'owner');
  readonly isViewer = computed(() => this.roleCodeOfSession() === 'viewer');
  private readonly serverForbidden = signal(false);
  readonly forbidden = computed(
    () => !(this.canManage() || this.isViewer()) || this.serverForbidden(),
  );

  // Filters and paging (AS-06: API query only, not the URL).
  readonly searchText = signal('');
  private readonly searchTerm = signal('');
  readonly roleFilter = signal<string | null>(null);
  readonly branchFilter = signal<string | null>(null);
  readonly statusFilter = signal<UserStatusFilter | null>(null);
  readonly sort = signal<UserSort>('name');
  readonly page = signal(1);
  readonly hasFilters = computed(
    () =>
      this.searchText().trim().length > 0 ||
      this.roleFilter() !== null ||
      this.branchFilter() !== null ||
      this.statusFilter() !== null,
  );
  private readonly query = computed<UserListQuery>(() => ({
    search: this.searchTerm(),
    roleCode: this.roleFilter(),
    branchId: this.branchFilter(),
    status: this.statusFilter(),
    sort: this.sort(),
    page: this.page(),
  }));

  readonly rows = signal<readonly UserRow[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(true);
  readonly listError = signal<ApiError | null>(null);
  readonly first = computed(() => (this.page() - 1) * PAGE_SIZE);

  readonly summary = signal<UserSummary | null>(null);
  readonly summaryLoading = signal(true);
  readonly summaryFailed = signal(false);

  readonly matrix = signal<PermissionMatrixData | null>(null);
  readonly matrixLoading = signal(true);
  readonly matrixFailed = signal(false);
  readonly roles = computed(() => this.matrix()?.roles ?? FALLBACK_ROLES);

  readonly branches = signal<readonly BranchRef[]>([]);

  readonly drawerOpen = signal(false);
  readonly drawerMode = signal<UserDrawerMode>('invite');
  readonly editingRow = signal<UserRow | null>(null);
  readonly mutatingId = signal<string | null>(null);
  readonly menuModel = signal<MenuItem[]>([]);
  readonly sessionExpired = signal(false);

  constructor() {
    this.listRequests
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.listError.set(null);
        }),
        switchMap((query) =>
          this.usersService.list(query).pipe(
            map((response): ListResult => ({ ok: true, response })),
            catchError((error: unknown) =>
              of<ListResult>({ ok: false, error: isApiError(error) ? error : null }),
            ),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handleListResult(result));

    this.searchInput
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        map((value) => value.trim()),
        filter((value) => value !== this.searchTerm()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((value) => {
        this.searchTerm.set(value);
        this.page.set(1);
        this.loadList();
      });

    if (!this.forbidden()) {
      this.loadList();
      this.loadSummary();
      this.loadMatrix();
      this.loadBranches();
    } else {
      this.loading.set(false);
    }
  }

  // Data loading

  loadList(): void {
    this.listRequests.next(this.query());
  }

  private handleListResult(result: ListResult): void {
    if (result.ok) {
      const { items, totalCount } = result.response;
      if (items.length === 0 && totalCount > 0 && this.page() > 1) {
        this.page.set(Math.max(1, Math.ceil(totalCount / PAGE_SIZE)));
        this.loadList();
        return;
      }
      this.loading.set(false);
      this.rows.set(items);
      this.totalCount.set(totalCount);
      return;
    }
    this.loading.set(false);
    if (result.error?.kind === 'unauthorized') {
      this.onUnauthorized();
      return;
    }
    if (result.error?.kind === 'forbidden') {
      this.serverForbidden.set(true);
      return;
    }
    this.listError.set(
      result.error ?? { kind: 'unknown', status: 0, message: LIST_ERROR_MESSAGE, fieldErrors: {} },
    );
  }

  loadSummary(): void {
    this.summaryFailed.set(false);
    if (this.summary() === null) {
      this.summaryLoading.set(true);
    }
    this.usersService
      .summary()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (summary) => {
          this.summaryLoading.set(false);
          this.summary.set(summary);
        },
        error: (error: unknown) => {
          this.summaryLoading.set(false);
          this.summaryFailed.set(true);
          this.handleSecondaryFailure(error);
        },
      });
  }

  loadMatrix(): void {
    this.matrixLoading.set(true);
    this.matrixFailed.set(false);
    this.matrixService
      .get()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (matrix) => {
          this.matrixLoading.set(false);
          this.matrix.set(matrix);
        },
        error: (error: unknown) => {
          this.matrixLoading.set(false);
          this.matrixFailed.set(true);
          this.handleSecondaryFailure(error);
        },
      });
  }

  private loadBranches(): void {
    this.branchesService
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) =>
          this.branches.set(
            response.items
              .filter((branch) => branch.isActive)
              .map((branch) => ({ id: branch.id, name: branch.name })),
          ),
        error: (error: unknown) => this.handleSecondaryFailure(error),
      });
  }

  private handleSecondaryFailure(error: unknown): void {
    if (isApiError(error) && error.kind === 'unauthorized') {
      this.onUnauthorized();
    }
  }

  private refresh(): void {
    this.loadList();
    this.loadSummary();
  }

  // Filters, sorting, paging

  onSearchText(value: string): void {
    this.searchText.set(value);
    this.searchInput.next(value);
  }

  onRoleFilter(code: string | null): void {
    this.roleFilter.set(code);
    this.filtersChanged();
  }

  onBranchFilter(id: string | null): void {
    this.branchFilter.set(id);
    this.filtersChanged();
  }

  onStatusFilter(status: UserStatusFilter | null): void {
    this.statusFilter.set(status);
    this.filtersChanged();
  }

  clearFilters(): void {
    this.searchText.set('');
    this.searchTerm.set('');
    this.roleFilter.set(null);
    this.branchFilter.set(null);
    this.statusFilter.set(null);
    this.filtersChanged();
  }

  toggleSort(): void {
    this.sort.set(this.sort() === 'name' ? '-name' : 'name');
    this.page.set(1);
    this.loadList();
  }

  onPageChange(event: PaginatorState): void {
    this.page.set((event.page ?? 0) + 1);
    this.loadList();
  }

  private filtersChanged(): void {
    this.page.set(1);
    this.loadList();
  }

  // Drawer

  openInvite(): void {
    this.showDrawer('invite', null);
  }

  openEdit(row: UserRow): void {
    this.showDrawer('edit', row);
  }

  /** Switching the open drawer while it has unsaved edits asks first. */
  private showDrawer(mode: UserDrawerMode, row: UserRow | null): void {
    const show = () => {
      this.drawerMode.set(mode);
      this.editingRow.set(row);
      this.drawerOpen.set(true);
    };
    if (this.drawerOpen() && (this.drawer()?.isDirty() ?? false)) {
      this.confirmDiscard().subscribe((leave) => {
        if (leave) {
          show();
        }
      });
      return;
    }
    show();
  }

  onDrawerClosed(): void {
    this.drawerOpen.set(false);
  }

  onDrawerSaved(): void {
    this.refresh();
  }

  /** Consulted by `usersUnsavedChangesGuard` on route leave. */
  canLeave(): boolean | Observable<boolean> {
    if (this.sessionExpired()) {
      return true;
    }
    return this.drawerOpen() && (this.drawer()?.isDirty() ?? false) ? this.confirmDiscard() : true;
  }

  private confirmDiscard(): Observable<boolean> {
    return new Observable<boolean>((subscriber) => {
      this.confirmationService.confirm({
        header: 'Discard unsaved changes?',
        message: 'You have unsaved changes. Do you want to discard them?',
        defaultFocus: 'reject',
        acceptButtonProps: { label: 'Discard', severity: 'danger' },
        rejectButtonProps: { label: 'Keep editing', severity: 'secondary', outlined: true },
        accept: () => {
          subscriber.next(true);
          subscriber.complete();
        },
        reject: () => {
          subscriber.next(false);
          subscriber.complete();
        },
      });
    });
  }

  onUnauthorized(): void {
    handleUnauthorized(this.router, this.sessionExpired);
  }

  // Row actions (BR-12)

  openMenu(event: Event, row: UserRow): void {
    this.menuModel.set(this.buildMenu(row));
    this.menu().toggle(event);
  }

  buildMenu(row: UserRow): MenuItem[] {
    const busy = this.mutatingId() !== null;
    if (row.kind === 'invitation') {
      return [
        {
          label: 'Resend invitation',
          icon: 'pi pi-send',
          disabled: busy,
          command: () => this.resend(row),
        },
        {
          label: 'Revoke invitation',
          icon: 'pi pi-times-circle',
          disabled: busy,
          command: () => this.confirmRevoke(row),
        },
      ];
    }
    if (displayStatus(row) === 'suspended') {
      return [
        {
          label: 'Reactivate',
          icon: 'pi pi-refresh',
          disabled: busy,
          command: () => this.reactivate(row),
        },
      ];
    }
    const tooltip = row.isCurrentUser
      ? SELF_SUSPEND_TOOLTIP
      : row.isLastOwner
        ? LAST_OWNER_TOOLTIP
        : undefined;
    return [
      {
        label: 'Suspend access',
        icon: 'pi pi-ban',
        disabled: busy || tooltip !== undefined,
        tooltip,
        title: tooltip,
        tooltipPosition: 'left',
        command: () => this.confirmSuspend(row),
      },
    ];
  }

  private confirmSuspend(row: UserRow): void {
    const name = fullName(row);
    this.confirmationService.confirm({
      header: 'Suspend access',
      message: `Suspend ${name}? They won't be able to sign in. Assigned work and audit history are preserved.`,
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Suspend', severity: 'danger' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () =>
        this.mutate(row, this.usersService.suspend(row.id), `Access suspended for ${name}`),
    });
  }

  private confirmRevoke(row: UserRow): void {
    this.confirmationService.confirm({
      header: 'Revoke invitation',
      message: `Revoke the invitation for ${row.email}? The link will stop working.`,
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Revoke', severity: 'danger' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () => this.mutate(row, this.usersService.revoke(row.id), 'Invitation revoked'),
    });
  }

  private resend(row: UserRow): void {
    this.mutate(row, this.usersService.resend(row.id), `Invitation resent to ${row.email}`);
  }

  private reactivate(row: UserRow): void {
    this.mutate(
      row,
      this.usersService.reactivate(row.id),
      `Access reactivated for ${fullName(row)}`,
    );
  }

  private mutate(row: UserRow, request$: Observable<unknown>, success: string): void {
    if (this.mutatingId() !== null) {
      return;
    }
    this.mutatingId.set(row.id);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.mutatingId.set(null);
        this.messageService.add({ severity: 'success', summary: success });
        this.refresh();
      },
      error: (error: unknown) => this.handleMutationFailed(error, row),
    });
  }

  private handleMutationFailed(error: unknown, row: UserRow): void {
    this.mutatingId.set(null);
    const apiError: ApiError | null = isApiError(error) ? error : null;
    let summary: string;
    let reload = false;

    switch (apiError?.kind) {
      case 'unauthorized':
        this.onUnauthorized();
        return;
      case 'not-found':
        summary = USER_UNAVAILABLE_MESSAGE;
        reload = true;
        break;
      case 'conflict':
        if (row.kind === 'invitation') {
          summary = NOT_PENDING_MESSAGE;
          reload = true;
        } else {
          summary = row.isCurrentUser ? SELF_SUSPEND_TOOLTIP : LAST_OWNER_TOOLTIP;
        }
        break;
      default:
        summary =
          apiError?.status === 502
            ? DELIVERY_FAILED_MESSAGE
            : (apiError?.message ?? 'An unexpected error occurred. Please try again.');
    }
    this.messageService.add({ severity: 'error', summary });
    if (reload) {
      this.refresh();
    }
  }
}
