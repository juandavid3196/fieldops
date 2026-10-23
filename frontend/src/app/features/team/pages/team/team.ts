import { Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
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
  skip,
  switchMap,
  tap,
} from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { AvailabilityTab } from '../../components/availability-tab/availability-tab';
import { LinkAccountDialog } from '../../components/link-account-dialog/link-account-dialog';
import { SkillsTab } from '../../components/skills-tab/skills-tab';
import { TeamTable } from '../../components/team-table/team-table';
import { TeamTabs } from '../../components/team-tabs/team-tabs';
import { TechnicianDrawer } from '../../components/technician-drawer/technician-drawer';
import {
  TechnicianFormDrawer,
  TechnicianFormMode,
} from '../../components/technician-form-drawer/technician-form-drawer';
import {
  SCHEDULE_PATH,
  SKILLS_PATH,
  TechnicianProfile,
} from '../../components/technician-profile/technician-profile';
import { TeamTab } from '../../models/team-planning.model';
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
import {
  TeamUrlState,
  needsCanonicalUrl,
  parseTeamUrl,
  sameTeamUrlState,
  teamUrlParams,
} from '../../utils/team-url-state';
import { HEALTH_LABELS, HEALTH_SEVERITIES } from '../../utils/team-format';

import { localizedList, t } from '../../../../core/i18n/translate';
import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';
export const FORBIDDEN_MESSAGE = (): string => t('team.team.forbiddenMessage');
export const NOT_LINKED_MESSAGE = (): string => t('team.team.notLinkedMessage');
export const DESCRIPTION_MESSAGE = (): string => t('team.team.descriptionMessage');
export const SETTINGS_NOTE = (): string => t('team.team.settingsNote');
export const LOAD_ERROR_MESSAGE = (): string => t('team.team.loadErrorMessage');
export const EMPTY_INITIAL_MESSAGE = (): string => t('team.team.emptyInitialMessage');
export const EMPTY_FILTERED_MESSAGE = (): string => t('team.team.emptyFilteredMessage');
export const DEACTIVATE_CONFLICT_MESSAGE = (): string => t('team.team.deactivateConflictMessage');
export const ACTION_FAILED_MESSAGE = (): string => t('team.team.actionFailedMessage');
const SEARCH_DEBOUNCE_MS = 300;
const SKELETON_ROWS = [0, 1, 2, 3, 4, 5];
const SKELETON_CARDS = [0, 1, 2, 3, 4];
const MUTATE_ROLES: readonly string[] = ['owner', 'operations_manager'];

interface Choice<T> {
  readonly code: T;
  readonly label: string;
}

const STATUS_OPTIONS: Choice<StatusFilter>[] = localizedList(() => [
  { code: 'all_active', label: t('team.team.allActive') },
  { code: 'available', label: t('team.team.available') },
  { code: 'on_job', label: t('team.team.onJob') },
  { code: 'break', label: t('team.team.break') },
  { code: 'time_off', label: t('team.team.timeOff') },
  { code: 'off', label: t('team.team.off') },
  { code: 'inactive', label: t('common.inactive') },
  { code: 'suspended', label: t('team.team.suspended') },
]);
const LINK_OPTIONS: Choice<AccountLinkFilter>[] = localizedList(() => [
  { code: 'all', label: t('team.team.allAccounts') },
  { code: 'linked', label: t('team.team.linked') },
  { code: 'not_linked', label: t('team.team.notLinked') },
]);

type MetricKey = 'activeProfiles' | 'availableNow' | 'onJobs' | 'atCapacity' | 'unlinkedAccounts';

const METRIC_CARDS: readonly {
  readonly key: MetricKey;
  readonly label: string;
  readonly icon: string;
  readonly tone: string;
}[] = localizedList(() => [
  {
    key: 'activeProfiles',
    label: t('team.team.activeProfiles'),
    icon: 'pi-users',
    tone: 'primary',
  },
  { key: 'availableNow', label: t('team.team.availableNow'), icon: 'pi-user', tone: 'success' },
  { key: 'onJobs', label: t('team.team.onJobs'), icon: 'pi-briefcase', tone: 'info' },
  { key: 'atCapacity', label: t('team.team.atCapacity'), icon: 'pi-clock', tone: 'danger' },
  {
    key: 'unlinkedAccounts',
    label: t('team.team.unlinkedAccounts'),
    icon: 'pi-link',
    tone: 'warning',
  },
]);

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
    LocalizedRefPipe,
    TranslocoPipe,
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
    AvailabilityTab,
    LinkAccountDialog,
    SkillsTab,
    TeamTable,
    TeamTabs,
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

  get forbiddenMessage(): string {
    return FORBIDDEN_MESSAGE();
  }
  get notLinkedMessage(): string {
    return NOT_LINKED_MESSAGE();
  }
  get descriptionMessage(): string {
    return DESCRIPTION_MESSAGE();
  }
  get settingsNote(): string {
    return SETTINGS_NOTE();
  }
  get loadErrorMessage(): string {
    return LOAD_ERROR_MESSAGE();
  }
  get emptyInitialMessage(): string {
    return EMPTY_INITIAL_MESSAGE();
  }
  get emptyFilteredMessage(): string {
    return EMPTY_FILTERED_MESSAGE();
  }
  readonly pageSize = TEAM_PAGE_SIZE;
  readonly metricCards = METRIC_CARDS;
  readonly skeletonRows = SKELETON_ROWS;
  readonly skeletonCards = SKELETON_CARDS;
  readonly statusOptions = STATUS_OPTIONS;
  readonly linkOptions = LINK_OPTIONS;
  readonly healthLabels = HEALTH_LABELS;
  readonly healthSeverities = HEALTH_SEVERITIES;
  readonly skillsPath = SKILLS_PATH;
  readonly schedulePath = SCHEDULE_PATH;
  /** The technician shell's top bar already names the page, so it hides the breadcrumb. */
  private readonly route = inject(ActivatedRoute);
  readonly showCrumbs = this.route.snapshot.data['hideCrumbs'] !== true;
  /** BR-01: the selected tab lives in the `tab` query parameter. */
  readonly tab = toSignal(
    this.route.queryParamMap.pipe(
      map((params): TeamTab => {
        const value = params.get('tab');
        return value === 'availability' || value === 'skills' ? value : 'members';
      }),
    ),
    { initialValue: 'members' as TeamTab },
  );

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
  /** BR-02: the list state starts from the URL query; invalid values fall back to defaults. */
  private readonly initialState = parseTeamUrl(this.route.snapshot.queryParamMap);
  readonly period = signal<TeamPeriod>(this.initialState.period);
  readonly searchText = signal(this.initialState.q);
  private readonly searchTerm = signal(this.initialState.q);
  readonly branchId = signal<string | null>(this.initialState.branch);
  readonly skillId = signal<string | null>(this.initialState.skill);
  readonly status = signal<StatusFilter>(this.initialState.status);
  readonly accountLink = signal<AccountLinkFilter>(this.initialState.account);
  readonly sort = signal<TeamSort>(this.initialState.sort);
  readonly page = signal(this.initialState.page);
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
    return t('team.team.showingOfTechnicians', { from: from, to: to, total: total });
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
  private optionsLoaded = false;
  /** List states this page wrote to the URL whose query emissions are still to arrive. */
  private readonly expectedEchoes: TeamUrlState[] = [];
  /** Branch or skill ids from the URL are checked against the options before the first request. */
  private awaitingOptions = false;

  readonly capacityText = computed(() => {
    const alert = this.metrics()?.capacityAlert;
    if (!alert) {
      return null;
    }
    const when = this.period() === 'today' ? 'today' : t('team.team.thisWeek2');
    return alert.noAvailability || alert.workloadPercent == null
      ? t('team.team.hasNoAvailability', { name: alert.name, when: when })
      : t('team.team.isAtCapacity', {
          name: alert.name,
          workloadPercent: alert.workloadPercent,
          when: when,
        });
  });
  readonly branchChoices = computed<Choice<string>[]>(() => [
    { code: 'all', label: t('team.team.allBranches') },
    ...this.branches().map((branch) => ({ code: branch.id, label: branch.name })),
  ]);
  readonly skillChoices = computed<Choice<string>[]>(() => [
    { code: 'all', label: t('team.team.allSkills') },
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
        this.filtersChanged(true);
      });

    if (this.forbidden()) {
      this.loading.set(false);
      this.metricsLoading.set(false);
      this.coverageLoading.set(false);
      this.optionsLoading.set(false);
    } else if (this.isTechnician()) {
      this.loadOwn();
    } else {
      this.startListing();
    }
  }

  private startListing(): void {
    this.writeUrl(true);
    this.awaitingOptions = this.branchId() !== null || this.skillId() !== null;
    this.loadOptions();
    if (!this.awaitingOptions) {
      this.refresh();
    }
    // Back, Forward and edited URLs re-apply the list state; our own navigations echo the same state.
    this.route.queryParamMap
      .pipe(skip(1), takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => this.applyUrlState(parseTeamUrl(params)));
  }

  private urlState(): TeamUrlState {
    return {
      q: this.searchTerm(),
      branch: this.branchId(),
      skill: this.skillId(),
      status: this.status(),
      account: this.accountLink(),
      sort: this.sort(),
      period: this.period(),
      page: this.page(),
    };
  }

  /** Writes the list state to the URL: replacing while typing or correcting, pushing otherwise. */
  private writeUrl(replace: boolean): void {
    const state = this.urlState();
    const params = teamUrlParams(state);
    if (!needsCanonicalUrl(this.route.snapshot.queryParamMap, params)) {
      return;
    }
    this.expectedEchoes.push(state);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: params,
      queryParamsHandling: 'merge',
      replaceUrl: replace,
    });
  }

  private applyUrlState(state: TeamUrlState): void {
    const echo = this.expectedEchoes.findIndex((expected) => sameTeamUrlState(expected, state));
    if (echo >= 0) {
      this.expectedEchoes.splice(0, echo + 1);
      return;
    }
    this.expectedEchoes.length = 0;
    if (this.awaitingOptions || sameTeamUrlState(state, this.urlState())) {
      return;
    }
    const known = (id: string | null, options: readonly TeamOption[]): string | null =>
      id !== null && (!this.optionsLoaded || options.some((option) => option.id === id))
        ? id
        : null;
    const branch = known(state.branch, this.branches());
    const skill = known(state.skill, this.skills());
    const branchChanged = branch !== this.branchId();
    this.searchText.set(state.q);
    this.searchTerm.set(state.q);
    this.branchId.set(branch);
    this.skillId.set(skill);
    this.status.set(state.status);
    this.accountLink.set(state.account);
    this.sort.set(state.sort);
    this.period.set(state.period);
    this.page.set(state.page);
    if (branch !== state.branch || skill !== state.skill) {
      this.writeUrl(true);
    }
    this.loadList();
    this.loadMetrics();
    if (branchChanged) {
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
        this.writeUrl(true);
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
        message: LOAD_ERROR_MESSAGE(),
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
          this.optionsLoaded = true;
          this.finishAwaitingOptions();
        },
        error: (error: unknown) => {
          this.optionsLoading.set(false);
          this.optionsFailed.set(true);
          if (!this.handleAccessFailure(error)) {
            this.finishAwaitingOptions();
          }
        },
      });
  }

  /** BR-02: ids not in the Team options are replaced by their defaults before the first request. */
  private finishAwaitingOptions(): void {
    if (!this.awaitingOptions) {
      return;
    }
    this.awaitingOptions = false;
    const branch = this.branches().some((option) => option.id === this.branchId())
      ? this.branchId()
      : this.optionsLoaded
        ? null
        : this.branchId();
    const skill = this.skills().some((option) => option.id === this.skillId())
      ? this.skillId()
      : this.optionsLoaded
        ? null
        : this.skillId();
    if (branch !== this.branchId() || skill !== this.skillId()) {
      this.branchId.set(branch);
      this.skillId.set(skill);
      this.writeUrl(true);
    }
    this.refresh();
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
    this.writeUrl(false);
    this.loadList();
  }

  private filtersChanged(replaceUrl = false): void {
    this.page.set(1);
    this.writeUrl(replaceUrl);
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
      summary: t('team.team.thisTechnicianProfileIsnt'),
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
          subject: t('team.team.thisTechnicianProfile'),
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

  /** A tab's data request was refused with `403`: the existing forbidden state. */
  onTabForbidden(): void {
    this.serverForbidden.set(true);
  }

  // Account link and status (BR-17, BR-18)

  openLink(id: string, name: string): void {
    this.linkTarget.set({ id, name });
    this.linkOpen.set(true);
  }

  openLinkFromDrawer(id: string): void {
    const name =
      this.rows().find((row) => row.id === id)?.fullName ?? t('team.team.thisTechnician');
    this.openLink(id, name);
  }

  onLinked(): void {
    this.refresh();
    this.detailDrawer()?.reload();
  }

  openUnlinkFromDrawer(id: string): void {
    const name =
      this.rows().find((row) => row.id === id)?.fullName ?? t('team.team.thisTechnician');
    this.confirmUnlink(id, name);
  }

  private confirmUnlink(id: string, name: string): void {
    this.confirmationService.confirm({
      header: t('team.team.unlinkSUserAccount', { name: name }),
      message: t('team.team.theProfileStaysActive'),
      defaultFocus: 'reject',
      acceptButtonProps: { label: t('team.team.unlinkAccount') },
      rejectButtonProps: { label: t('common.cancel'), severity: 'secondary', outlined: true },
      accept: () => this.mutate(id, this.team.unlink(id), t('team.team.userAccountUnlinked')),
    });
  }

  private confirmStatus(row: TechnicianRow, activate: boolean): void {
    this.confirmationService.confirm({
      header: `${activate ? t('team.team.activate') : t('team.team.deactivate')} ${row.fullName}?`,
      message: activate ? t('team.team.theProfileWillReturn') : t('team.team.theProfileWillLeave'),
      defaultFocus: 'reject',
      acceptButtonProps: { label: activate ? t('team.team.activate') : t('team.team.deactivate') },
      rejectButtonProps: { label: t('common.cancel'), severity: 'secondary', outlined: true },
      accept: () =>
        this.mutate(
          row.id,
          activate ? this.team.activate(row.id) : this.team.deactivate(row.id),
          activate ? t('team.team.profileActivated') : t('team.team.profileDeactivated'),
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
            summary: DEACTIVATE_CONFLICT_MESSAGE(),
            detail:
              count === undefined
                ? undefined
                : t('team.team.upcomingAssigned', {
                    count: count,
                    value: count === 1 ? t('team.team.visitIs') : t('team.team.visitsAre'),
                  }),
          });
          this.refresh();
        } else if (apiError?.kind === 'not-found') {
          this.onTechnicianUnavailable();
        } else {
          this.messageService.add({ severity: 'error', summary: ACTION_FAILED_MESSAGE() });
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
      label: t('common.view'),
      icon: 'pi pi-eye',
      command: () => this.openDetail(row.id),
    };
    const profile: MenuItem = {
      label: t('team.team.viewProfile'),
      icon: 'pi pi-user',
      command: () => void this.router.navigate(['/team', row.id, 'overview']),
    };
    if (!this.canMutate()) {
      return [view, profile];
    }
    const busy = this.mutatingId() !== null;
    return [
      view,
      profile,
      { label: t('common.edit'), icon: 'pi pi-pencil', command: () => this.openEdit(row.id) },
      row.isLinked
        ? {
            label: t('team.team.unlinkAccount'),
            icon: 'pi pi-link',
            disabled: busy,
            command: () => this.confirmUnlink(row.id, row.fullName),
          }
        : {
            label: t('team.team.linkAccount'),
            icon: 'pi pi-link',
            disabled: busy,
            command: () => this.openLink(row.id, row.fullName),
          },
      row.profileStatus === 'active'
        ? {
            label: t('team.team.deactivate'),
            icon: 'pi pi-ban',
            disabled: busy,
            command: () => this.confirmStatus(row, false),
          }
        : {
            label: t('team.team.activate'),
            icon: 'pi pi-replay',
            disabled: busy,
            command: () => this.confirmStatus(row, true),
          },
    ];
  }
}
