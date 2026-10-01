import { Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService, MenuItem, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Menu } from 'primeng/menu';
import { Message } from 'primeng/message';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
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

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { LinkAccountDialog } from '../../components/link-account-dialog/link-account-dialog';
import { TeamTable } from '../../components/team-table/team-table';
import { TechnicianDrawer } from '../../components/technician-drawer/technician-drawer';
import {
  TechnicianFormDrawer,
  TechnicianFormMode,
} from '../../components/technician-form-drawer/technician-form-drawer';
import {
  AVAILABILITY_PATH,
  SCHEDULE_PATH,
  SKILLS_PATH,
  TechnicianProfile,
} from '../../components/technician-profile/technician-profile';
import {
  AccountLinkFilter,
  SkillCoverage,
  StatusFilter,
  TEAM_PAGE_SIZE,
  TeamMetrics,
  TeamOption,
  TeamPeriod,
  TeamSort,
  TechnicianDetail,
  TechnicianListQuery,
  TechnicianListResponse,
  TechnicianRow,
} from '../../models/team.model';
import { TeamService } from '../../services/team.service';
import { HEALTH_LABELS, HEALTH_SEVERITIES } from '../../utils/team-format';

export const FORBIDDEN_MESSAGE = "You don't have access to Team.";
export const NOT_LINKED_MESSAGE = "Your team profile isn't linked yet. Contact your manager.";
export const DESCRIPTION_MESSAGE =
  'Manage technician profiles, skills, availability, and operational workload.';
export const SETTINGS_NOTE = 'Sign-in access and permissions are managed in Settings.';
export const LOAD_ERROR_MESSAGE = "We couldn't load this section.";
export const EMPTY_INITIAL_MESSAGE = 'No technician profiles yet.';
export const EMPTY_FILTERED_MESSAGE = 'No technicians match your filters.';
export const DEACTIVATE_CONFLICT_MESSAGE =
  "Reassign this technician's upcoming visits before deactivating the profile.";
export const ACTION_FAILED_MESSAGE = "We couldn't update the technician profile. Try again.";
const SEARCH_DEBOUNCE_MS = 300;
const SKELETON_ROWS = [0, 1, 2, 3, 4, 5];
const SKELETON_CARDS = [0, 1, 2, 3, 4];
const MUTATE_ROLES: readonly string[] = ['owner', 'operations_manager'];

interface Choice<T> {
  readonly code: T;
  readonly label: string;
}

const STATUS_OPTIONS: Choice<StatusFilter>[] = [
  { code: 'all_active', label: 'All active' },
  { code: 'available', label: 'Available' },
  { code: 'on_job', label: 'On job' },
  { code: 'break', label: 'Break' },
  { code: 'time_off', label: 'Time off' },
  { code: 'off', label: 'Off' },
  { code: 'inactive', label: 'Inactive' },
  { code: 'suspended', label: 'Suspended' },
];
const LINK_OPTIONS: Choice<AccountLinkFilter>[] = [
  { code: 'all', label: 'All accounts' },
  { code: 'linked', label: 'Linked' },
  { code: 'not_linked', label: 'Not linked' },
];

type MetricKey = 'activeProfiles' | 'availableNow' | 'onJobs' | 'atCapacity' | 'unlinkedAccounts';

const METRIC_CARDS: readonly {
  readonly key: MetricKey;
  readonly label: string;
  readonly icon: string;
  readonly tone: string;
}[] = [
  { key: 'activeProfiles', label: 'Active profiles', icon: 'pi-users', tone: 'primary' },
  { key: 'availableNow', label: 'Available now', icon: 'pi-user', tone: 'success' },
  { key: 'onJobs', label: 'On jobs', icon: 'pi-briefcase', tone: 'info' },
  { key: 'atCapacity', label: 'At capacity', icon: 'pi-clock', tone: 'danger' },
  { key: 'unlinkedAccounts', label: 'Unlinked accounts', icon: 'pi-link', tone: 'warning' },
];

type ListResult =
  | { readonly ok: true; readonly response: TechnicianListResponse }
  | { readonly ok: false; readonly error: ApiError | null };

type Panel = 'none' | 'detail' | 'form';

/**
 * Team page (`/team`): metrics, alerts, filterable server-paged table, skill coverage, detail and
 * form drawers, link dialog and status actions. Owner/Operations Manager mutate, Dispatcher reads,
 * Technician sees only their own profile, any other role is forbidden with no data requests
 * (BR-20, BR-21). Backend authorization is authoritative.
 */
@Component({
  selector: 'app-team',
  imports: [
    FormsModule,
    RouterLink,
    ButtonDirective,
    ConfirmDialog,
    DiscardChangesDialog,
    IconField,
    InputIcon,
    InputText,
    Menu,
    Message,
    Paginator,
    Select,
    Skeleton,
    Tag,
    Toast,
    LinkAccountDialog,
    TeamTable,
    TechnicianDrawer,
    TechnicianFormDrawer,
    TechnicianProfile,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './team.html',
  styleUrl: './team.scss',
})
export class Team {
  private readonly team = inject(TeamService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly menu = viewChild.required(Menu);
  private readonly formDrawer = viewChild(TechnicianFormDrawer);
  private readonly detailDrawer = viewChild(TechnicianDrawer);

  private readonly listRequests = new Subject<TechnicianListQuery>();
  private readonly searchInput = new Subject<string>();

  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly notLinkedMessage = NOT_LINKED_MESSAGE;
  readonly descriptionMessage = DESCRIPTION_MESSAGE;
  readonly settingsNote = SETTINGS_NOTE;
  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
  readonly emptyInitialMessage = EMPTY_INITIAL_MESSAGE;
  readonly emptyFilteredMessage = EMPTY_FILTERED_MESSAGE;
  readonly pageSize = TEAM_PAGE_SIZE;
  readonly metricCards = METRIC_CARDS;
  readonly skeletonRows = SKELETON_ROWS;
  readonly skeletonCards = SKELETON_CARDS;
  readonly statusOptions = STATUS_OPTIONS;
  readonly linkOptions = LINK_OPTIONS;
  readonly healthLabels = HEALTH_LABELS;
  readonly healthSeverities = HEALTH_SEVERITIES;
  readonly skillsPath = SKILLS_PATH;
  readonly availabilityPath = AVAILABILITY_PATH;
  readonly schedulePath = SCHEDULE_PATH;

  // Roles (UX only)
  private readonly roleCode = computed(() => this.sessionService.session()?.role.code ?? '');
  readonly canMutate = computed(() => MUTATE_ROLES.includes(this.roleCode()));
  readonly isOwner = computed(() => this.roleCode() === 'owner');
  readonly isTechnician = computed(() => this.roleCode() === 'technician');
  private readonly canRead = computed(() => this.canMutate() || this.roleCode() === 'dispatcher');
  private readonly serverForbidden = signal(false);
  readonly forbidden = computed(
    () => !(this.canRead() || this.isTechnician()) || this.serverForbidden(),
  );

  // Filters and paging
  readonly period = signal<TeamPeriod>('today');
  readonly searchText = signal('');
  private readonly searchTerm = signal('');
  readonly branchId = signal<string | null>(null);
  readonly skillId = signal<string | null>(null);
  readonly status = signal<StatusFilter>('all_active');
  readonly accountLink = signal<AccountLinkFilter>('all');
  readonly sort = signal<TeamSort>('name_asc');
  readonly page = signal(1);
  readonly hasFilters = computed(
    () =>
      this.searchText().trim().length > 0 ||
      this.branchId() !== null ||
      this.skillId() !== null ||
      this.status() !== 'all_active' ||
      this.accountLink() !== 'all',
  );

  readonly rows = signal<readonly TechnicianRow[]>([]);
  readonly totalCount = signal(0);
  readonly teamMembersCount = signal<number | null>(null);
  readonly loading = signal(true);
  readonly listError = signal<ApiError | null>(null);
  readonly first = computed(() => (this.page() - 1) * TEAM_PAGE_SIZE);
  readonly report = computed(() => {
    const total = this.totalCount();
    const from = total === 0 ? 0 : this.first() + 1;
    const to = Math.min(this.first() + TEAM_PAGE_SIZE, total);
    return `Showing ${from} – ${to} of ${total} technicians`;
  });

  readonly metrics = signal<TeamMetrics | null>(null);
  readonly metricsLoading = signal(true);
  readonly metricsFailed = signal(false);
  readonly coverage = signal<readonly SkillCoverage[]>([]);
  readonly coverageLoading = signal(true);
  readonly coverageFailed = signal(false);
  readonly branches = signal<readonly TeamOption[]>([]);
  readonly skills = signal<readonly TeamOption[]>([]);
  readonly optionsLoading = signal(true);
  private readonly optionsFailed = signal(false);

  readonly capacityText = computed(() => {
    const alert = this.metrics()?.capacityAlert;
    if (!alert) {
      return null;
    }
    const when = this.period() === 'today' ? 'today' : 'this week';
    return alert.noAvailability || alert.workloadPercent == null
      ? `${alert.name} has no availability ${when}.`
      : `${alert.name} is at ${alert.workloadPercent}% capacity ${when}.`;
  });
  readonly branchChoices = computed<Choice<string>[]>(() => [
    { code: 'all', label: 'All branches' },
    ...this.branches().map((branch) => ({ code: branch.id, label: branch.name })),
  ]);
  readonly skillChoices = computed<Choice<string>[]>(() => [
    { code: 'all', label: 'All skills' },
    ...this.skills().map((skill) => ({ code: skill.id, label: skill.name })),
  ]);

  // Panels and actions
  readonly panel = signal<Panel>('none');
  readonly detailId = signal<string | null>(null);
  readonly formMode = signal<TechnicianFormMode>('create');
  readonly formId = signal<string | null>(null);
  private formReturnTo: string | null = null;
  readonly linkOpen = signal(false);
  readonly linkTarget = signal<{ readonly id: string; readonly name: string } | null>(null);
  readonly menuModel = signal<MenuItem[]>([]);
  readonly mutatingId = signal<string | null>(null);
  readonly sessionExpired = signal(false);

  // Technician own profile (BR-20)
  readonly own = signal<TechnicianDetail | null>(null);
  readonly ownLoading = signal(false);
  readonly ownFailed = signal(false);
  readonly ownNotLinked = signal(false);

  constructor() {
    this.listRequests
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.listError.set(null);
        }),
        switchMap((query) =>
          this.team.list(query).pipe(
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
        this.filtersChanged();
      });

    if (this.forbidden()) {
      this.loading.set(false);
      this.metricsLoading.set(false);
      this.coverageLoading.set(false);
      this.optionsLoading.set(false);
    } else if (this.isTechnician()) {
      this.loadOwn();
    } else {
      this.loadOptions();
      this.loadList();
      this.loadMetrics();
      this.loadCoverage();
    }
  }

  metricValue(key: MetricKey): number | string {
    return this.metrics()?.[key] ?? '—';
  }

  // Data loading

  loadList(): void {
    this.listRequests.next({
      search: this.searchTerm(),
      branchId: this.branchId(),
      skillId: this.skillId(),
      status: this.status(),
      accountLink: this.accountLink(),
      sort: this.sort(),
      period: this.period(),
      page: this.page(),
    });
  }

  private handleListResult(result: ListResult): void {
    if (result.ok) {
      const { items, totalCount, teamMembersCount } = result.response;
      if (items.length === 0 && totalCount > 0 && this.page() > 1) {
        this.page.set(Math.max(1, Math.ceil(totalCount / TEAM_PAGE_SIZE)));
        this.loadList();
        return;
      }
      this.loading.set(false);
      this.rows.set(items);
      this.totalCount.set(totalCount);
      this.teamMembersCount.set(teamMembersCount);
      return;
    }
    this.loading.set(false);
    if (this.handleAccessFailure(result.error)) {
      return;
    }
    this.listError.set(
      result.error ?? {
        kind: 'unknown',
        status: 0,
        message: LOAD_ERROR_MESSAGE,
        fieldErrors: {},
      },
    );
  }

  /** 401 leaves to Sign In, 403 shows the forbidden state; returns true when handled. */
  private handleAccessFailure(error: unknown): boolean {
    const kind = isApiError(error) ? error.kind : null;
    if (kind === 'unauthorized') {
      this.onUnauthorized();
      return true;
    }
    if (kind === 'forbidden') {
      this.serverForbidden.set(true);
      return true;
    }
    return false;
  }

  loadMetrics(): void {
    this.metricsFailed.set(false);
    if (this.metrics() === null) {
      this.metricsLoading.set(true);
    }
    this.team
      .metrics(this.branchId(), this.period())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (metrics) => {
          this.metricsLoading.set(false);
          this.metrics.set(metrics);
        },
        error: (error: unknown) => {
          this.metricsLoading.set(false);
          if (!this.handleAccessFailure(error)) {
            this.metricsFailed.set(true);
          }
        },
      });
  }

  loadCoverage(): void {
    this.coverageFailed.set(false);
    this.coverageLoading.set(true);
    this.team
      .skillCoverage(this.branchId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (coverage) => {
          this.coverageLoading.set(false);
          this.coverage.set(coverage);
        },
        error: (error: unknown) => {
          this.coverageLoading.set(false);
          if (!this.handleAccessFailure(error)) {
            this.coverageFailed.set(true);
          }
        },
      });
  }

  loadOptions(): void {
    this.optionsLoading.set(true);
    this.optionsFailed.set(false);
    this.team
      .options()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (options) => {
          this.optionsLoading.set(false);
          this.branches.set(options.branches);
          this.skills.set(options.skills);
        },
        error: (error: unknown) => {
          this.optionsLoading.set(false);
          this.optionsFailed.set(true);
          this.handleAccessFailure(error);
        },
      });
  }

  loadOwn(): void {
    this.ownLoading.set(true);
    this.ownFailed.set(false);
    this.ownNotLinked.set(false);
    this.team
      .me()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.ownLoading.set(false);
          this.own.set(detail);
        },
        error: (error: unknown) => {
          this.ownLoading.set(false);
          const kind = isApiError(error) ? error.kind : null;
          if (kind === 'not-found') {
            this.ownNotLinked.set(true);
          } else if (!this.handleAccessFailure(error)) {
            this.ownFailed.set(true);
          }
        },
      });
  }

  /** Refreshes list, metrics and coverage after a change. */
  private refresh(): void {
    this.loadList();
    this.loadMetrics();
    this.loadCoverage();
  }

  // Filters, sorting, paging (BR-09, BR-10)

  onSearchText(value: string): void {
    this.searchText.set(value);
    this.searchInput.next(value);
  }

  onPeriod(period: TeamPeriod): void {
    if (period === this.period()) {
      return;
    }
    this.period.set(period);
    this.loadMetrics();
    this.filtersChanged();
  }

  /** The Branch filter also scopes metrics, alerts and coverage. */
  onBranch(code: string): void {
    this.branchId.set(code === 'all' ? null : code);
    this.loadMetrics();
    this.loadCoverage();
    this.filtersChanged();
  }

  onSkill(code: string): void {
    this.skillId.set(code === 'all' ? null : code);
    this.filtersChanged();
  }

  onStatus(status: StatusFilter): void {
    this.status.set(status);
    this.filtersChanged();
  }

  onAccountLink(value: AccountLinkFilter): void {
    this.accountLink.set(value);
    this.filtersChanged();
  }

  toggleNameSort(): void {
    this.sort.set(this.sort() === 'name_asc' ? 'name_desc' : 'name_asc');
    this.filtersChanged();
  }

  clearFilters(): void {
    const branchChanged = this.branchId() !== null;
    this.searchText.set('');
    this.searchTerm.set('');
    this.branchId.set(null);
    this.skillId.set(null);
    this.status.set('all_active');
    this.accountLink.set('all');
    if (branchChanged) {
      this.loadMetrics();
      this.loadCoverage();
    }
    this.filtersChanged();
  }

  onPageChange(event: PaginatorState): void {
    this.page.set((event.page ?? 0) + 1);
    this.loadList();
  }

  private filtersChanged(): void {
    this.page.set(1);
    this.loadList();
  }

  // Drawers

  /** Switching away from a dirty form asks first. */
  private whenFormClean(action: () => void): void {
    if (this.panel() === 'form' && (this.formDrawer()?.isDirty() ?? false)) {
      this.confirmDiscard().subscribe((leave) => {
        if (leave) {
          action();
        }
      });
      return;
    }
    action();
  }

  openDetail(id: string): void {
    this.whenFormClean(() => {
      this.formReturnTo = null;
      this.detailId.set(id);
      this.panel.set('detail');
    });
  }

  openCreate(): void {
    this.openForm('create', null, null);
  }

  openEdit(id: string, returnTo: string | null = null): void {
    this.openForm('edit', id, returnTo);
  }

  private openForm(mode: TechnicianFormMode, id: string | null, returnTo: string | null): void {
    this.whenFormClean(() => {
      if (this.optionsFailed()) {
        this.loadOptions();
      }
      this.formReturnTo = returnTo;
      this.formMode.set(mode);
      this.formId.set(id);
      this.panel.set('form');
    });
  }

  onDetailClosed(): void {
    this.panel.set('none');
  }

  onFormClosed(): void {
    const returnTo = this.formReturnTo;
    this.formReturnTo = null;
    if (returnTo !== null) {
      this.detailId.set(returnTo);
      this.panel.set('detail');
    } else {
      this.panel.set('none');
    }
  }

  onFormSaved(): void {
    this.refresh();
    this.detailDrawer()?.reload();
  }

  onTechnicianUnavailable(): void {
    this.messageService.add({
      severity: 'error',
      summary: "This technician profile isn't available.",
    });
    this.formReturnTo = null;
    this.panel.set('none');
    this.refresh();
  }

  /** Consulted by `teamUnsavedChangesGuard` on route leave. */
  canLeave(): boolean | Observable<boolean> {
    if (this.sessionExpired()) {
      return true;
    }
    return this.panel() === 'form' && (this.formDrawer()?.isDirty() ?? false)
      ? this.confirmDiscard()
      : true;
  }

  private confirmDiscard(): Observable<boolean> {
    return new Observable<boolean>((subscriber) => {
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: 'this technician profile',
          accept: () => {
            subscriber.next(true);
            subscriber.complete();
          },
          reject: () => {
            subscriber.next(false);
            subscriber.complete();
          },
        }),
      );
    });
  }

  onUnauthorized(): void {
    handleUnauthorized(this.router, this.sessionExpired);
  }

  // Account link and status (BR-17, BR-18)

  openLink(id: string, name: string): void {
    this.linkTarget.set({ id, name });
    this.linkOpen.set(true);
  }

  openLinkFromDrawer(id: string): void {
    const name = this.rows().find((row) => row.id === id)?.fullName ?? 'this technician';
    this.openLink(id, name);
  }

  onLinked(): void {
    this.refresh();
    this.detailDrawer()?.reload();
  }

  openUnlinkFromDrawer(id: string): void {
    const name = this.rows().find((row) => row.id === id)?.fullName ?? 'this technician';
    this.confirmUnlink(id, name);
  }

  private confirmUnlink(id: string, name: string): void {
    this.confirmationService.confirm({
      header: `Unlink ${name}'s user account?`,
      message:
        'The profile stays active. The user keeps their role, permissions and branch access.',
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Unlink account' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () => this.mutate(id, this.team.unlink(id), 'User account unlinked.'),
    });
  }

  private confirmStatus(row: TechnicianRow, activate: boolean): void {
    this.confirmationService.confirm({
      header: `${activate ? 'Activate' : 'Deactivate'} ${row.fullName}?`,
      message: activate
        ? 'The profile will return to active lists. Links, skills and availability are kept.'
        : 'The profile will leave active lists. Links, skills, availability and history are kept.',
      defaultFocus: 'reject',
      acceptButtonProps: { label: activate ? 'Activate' : 'Deactivate' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () =>
        this.mutate(
          row.id,
          activate ? this.team.activate(row.id) : this.team.deactivate(row.id),
          activate ? 'Profile activated.' : 'Profile deactivated.',
        ),
    });
  }

  private mutate(id: string, request$: Observable<unknown>, success: string): void {
    if (this.mutatingId() !== null) {
      return;
    }
    this.mutatingId.set(id);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.mutatingId.set(null);
        this.messageService.add({ severity: 'success', summary: success });
        this.refresh();
        this.detailDrawer()?.reload();
      },
      error: (error: unknown) => {
        this.mutatingId.set(null);
        const apiError = isApiError(error) ? error : null;
        if (apiError?.kind === 'unauthorized') {
          this.onUnauthorized();
        } else if (apiError?.kind === 'conflict') {
          const count = apiError.upcomingVisitCount;
          this.messageService.add({
            severity: 'error',
            summary: DEACTIVATE_CONFLICT_MESSAGE,
            detail:
              count === undefined
                ? undefined
                : `${count} upcoming ${count === 1 ? 'visit is' : 'visits are'} assigned.`,
          });
          this.refresh();
        } else if (apiError?.kind === 'not-found') {
          this.onTechnicianUnavailable();
        } else {
          this.messageService.add({ severity: 'error', summary: ACTION_FAILED_MESSAGE });
        }
      },
    });
  }

  // Row actions (BR-12)

  openMenu(event: Event, row: TechnicianRow): void {
    this.menuModel.set(this.buildMenu(row));
    this.menu().toggle(event);
  }

  buildMenu(row: TechnicianRow): MenuItem[] {
    const view: MenuItem = {
      label: 'View',
      icon: 'pi pi-eye',
      command: () => this.openDetail(row.id),
    };
    if (!this.canMutate()) {
      return [view];
    }
    const busy = this.mutatingId() !== null;
    return [
      view,
      { label: 'Edit', icon: 'pi pi-pencil', command: () => this.openEdit(row.id) },
      row.isLinked
        ? {
            label: 'Unlink account',
            icon: 'pi pi-link',
            disabled: busy,
            command: () => this.confirmUnlink(row.id, row.fullName),
          }
        : {
            label: 'Link account',
            icon: 'pi pi-link',
            disabled: busy,
            command: () => this.openLink(row.id, row.fullName),
          },
      row.profileStatus === 'active'
        ? {
            label: 'Deactivate',
            icon: 'pi pi-ban',
            disabled: busy,
            command: () => this.confirmStatus(row, false),
          }
        : {
            label: 'Activate',
            icon: 'pi pi-replay',
            disabled: busy,
            command: () => this.confirmStatus(row, true),
          },
    ];
  }
}
