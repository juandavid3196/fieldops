import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
  viewChildren,
  WritableSignal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ConfirmationService, MenuItem, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Menu } from 'primeng/menu';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import { Toast } from 'primeng/toast';
import { Observable, Subscription, map, of, switchMap } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { NewRequestDrawer } from '../../../requests/components/new-request-drawer/new-request-drawer';
import { CustomerOption, RequestOptions } from '../../../requests/models/requests.model';
import { RequestsService } from '../../../requests/services/requests.service';
import { ActivityTimeline } from '../../components/activity-timeline/activity-timeline';
import { AppointmentsCard } from '../../components/appointments-card/appointments-card';
import {
  BranchOption,
  CUSTOMER_UNAVAILABLE_MESSAGE,
  CustomerDrawer,
} from '../../components/customer-drawer/customer-drawer';
import { NotesCard } from '../../components/notes-card/notes-card';
import { PropertiesCard } from '../../components/properties-card/properties-card';
import {
  PROPERTY_UNAVAILABLE_MESSAGE,
  PropertyDrawer,
  PropertyDrawerMode,
} from '../../components/property-drawer/property-drawer';
import { RecentWorkCard } from '../../components/recent-work-card/recent-work-card';
import { RelatedListTab } from '../../components/related-list-tab/related-list-tab';
import { SideCards } from '../../components/side-cards/side-cards';
import {
  ActivityPeriod,
  ActivityResponse,
  ActivityType,
  AppointmentsResponse,
  CustomerFieldKey,
  CustomerOverview,
  CustomerTag,
  DetailTab,
  IDLE_REGION,
  JOBS_READ_ROLES,
  NOTES_PAGE_SIZE,
  NoteItem,
  PropertiesResponse,
  PortalAccess,
  PropertyItem,
  RELATED_KINDS,
  RELATED_READ_ROLES,
  RecentWorkResponse,
  RegionState,
  RelatedKind,
} from '../../models/customer.model';
import { CustomersService } from '../../services/customers.service';
import { groupActivity } from '../../utils/customer-activity';
import {
  formatMoney,
  initials,
  preferredContact,
  validateNote,
} from '../../utils/customer-detail-format';
import { STATUS_LABELS, formatPhone, statusSeverity } from '../../utils/customer-format';
import {
  ALREADY_ACTIVE_MESSAGE,
  ALREADY_ARCHIVED_MESSAGE,
  FORBIDDEN_MESSAGE,
  UPDATE_FAILED_MESSAGE,
} from '../customers/customers';

import { localizedList, t } from '../../../../core/i18n/translate';
export const NOT_FOUND_TITLE = (): string => t('customers.customerDetail.notFoundTitle');
export const NOT_FOUND_MESSAGE = (): string => t('customers.customerDetail.notFoundMessage');
export const DETAIL_ERROR_MESSAGE = (): string => t('customers.customerDetail.detailErrorMessage');
export const PROPERTY_UPDATE_FAILED_MESSAGE = (): string =>
  t('customers.customerDetail.propertyUpdateFailedMessage');
export const NOTE_FAILED_MESSAGE = (): string => t('customers.customerDetail.noteFailedMessage');
export const PORTAL_FAILED_MESSAGE = (): string =>
  t('customers.customerDetail.portalFailedMessage');
export const PORTAL_INVITE_UNAVAILABLE_MESSAGE = (): string =>
  t('customers.customerDetail.portalInviteUnavailableMessage');
const MUTATE_ROLES: readonly string[] = ['owner', 'dispatcher'];
const READ_ROLES: readonly string[] = ['operations_manager', 'accounting', 'viewer'];
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

interface TabInfo {
  readonly code: DetailTab;
  readonly label: string;
}

const TABS: readonly TabInfo[] = localizedList(() => [
  { code: 'overview', label: t('customers.customerDetail.overview') },
  { code: 'requests', label: t('customers.customerDetail.requests') },
  { code: 'quotes', label: t('customers.customerDetail.quotes') },
  { code: 'jobs', label: t('customers.customerDetail.jobs') },
  { code: 'invoices', label: t('customers.customerDetail.invoices') },
  { code: 'activity', label: t('customers.customerDetail.activity') },
]);

/** Create menu destinations; Request opens the New request drawer instead of navigating (BR-09). */
const CREATE_LINKS: readonly { readonly label: string; readonly path: string | null }[] =
  localizedList(() => [
    { label: t('customers.customerDetail.request'), path: null },
    { label: t('customers.customerDetail.quote'), path: '/quotes' },
    { label: t('customers.customerDetail.job'), path: '/jobs' },
    { label: t('customers.customerDetail.invoice'), path: '/invoices' },
  ]);

type PropertyAction = 'set-primary' | 'archive' | 'reactivate';

/**
 * Customer detail page (`/customers/:customerId`, FR-01). Single owner of the page state: header,
 * Overview regions, notes, Activity, both drawers and the archive/reactivate actions. Owner and
 * Dispatcher mutate, read roles view, any other role gets the forbidden state with no data
 * requests (BR-02).
 */
@Component({
  selector: 'app-customer-detail',
  imports: [
    TranslocoPipe,
    RouterLink,
    ButtonDirective,
    Menu,
    Message,
    Skeleton,
    Tag,
    Toast,
    ConfirmDialog,
    DiscardChangesDialog,
    ActivityTimeline,
    AppointmentsCard,
    NewRequestDrawer,
    RelatedListTab,
    CustomerDrawer,
    NotesCard,
    PropertiesCard,
    PropertyDrawer,
    RecentWorkCard,
    SideCards,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './customer-detail.html',
  styleUrl: './customer-detail.scss',
})
export class CustomerDetail {
  private readonly customers = inject(CustomersService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly requestsService = inject(RequestsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  private readonly customerDrawer = viewChild(CustomerDrawer);
  private readonly propertyDrawer = viewChild(PropertyDrawer);
  private readonly requestDrawer = viewChild(NewRequestDrawer);
  private readonly relatedLists = viewChildren(RelatedListTab);
  private readonly menu = viewChild.required(Menu);

  get forbiddenMessage(): string {
    return FORBIDDEN_MESSAGE();
  }
  get notFoundTitle(): string {
    return NOT_FOUND_TITLE();
  }
  get notFoundMessage(): string {
    return NOT_FOUND_MESSAGE();
  }
  get detailErrorMessage(): string {
    return DETAIL_ERROR_MESSAGE();
  }
  readonly relatedKinds = RELATED_KINDS;

  private readonly roleCode = computed(() => this.sessionService.session()?.role.code ?? '');
  readonly canMutate = computed(() => MUTATE_ROLES.includes(this.roleCode()));
  /** BR-03: tabs the role can read; Overview and Activity are always shown. */
  readonly visibleTabs = computed(() =>
    TABS.filter(
      (item) =>
        !(RELATED_KINDS as readonly string[]).includes(item.code) ||
        RELATED_READ_ROLES[item.code as RelatedKind].includes(this.roleCode()),
    ),
  );
  /** BR-14: without Jobs read the job-derived Overview values and regions are omitted. */
  readonly canReadJobs = computed(() => JOBS_READ_ROLES.includes(this.roleCode()));
  readonly isReadOnly = computed(() => READ_ROLES.includes(this.roleCode()));
  private readonly serverForbidden = signal(false);
  readonly forbidden = computed(
    () => !(this.canMutate() || this.isReadOnly()) || this.serverForbidden(),
  );
  readonly notFound = signal(false);
  readonly sessionExpired = signal(false);

  readonly customerId = signal<string | null>(null);
  readonly tab = signal<DetailTab>('overview');

  readonly overview = signal<RegionState<CustomerOverview>>(IDLE_REGION);
  readonly portalBusy = signal(false);
  readonly portalError = signal<string | null>(null);
  readonly properties = signal<RegionState<PropertiesResponse>>(IDLE_REGION);
  readonly recentWork = signal<RegionState<RecentWorkResponse>>(IDLE_REGION);
  readonly appointments = signal<RegionState<AppointmentsResponse>>(IDLE_REGION);
  readonly activity = signal<RegionState<ActivityResponse>>(IDLE_REGION);
  readonly activityType = signal<ActivityType | 'all'>('all');
  readonly activityPeriod = signal<ActivityPeriod>('last_90_days');
  /** Types of the latest response; kept while a filter change reloads. */
  readonly activityTypes = signal<readonly ActivityType[]>([]);
  readonly loadingOlder = signal(false);
  readonly olderError = signal(false);
  readonly focusEventId = signal<string | null>(null);
  /** Related tabs opened at least once stay mounted so their filters persist (BR-03). */
  readonly visitedKinds = signal<ReadonlySet<RelatedKind>>(new Set());

  readonly notes = signal<RegionState<readonly NoteItem[]>>(IDLE_REGION);
  readonly notesTotal = signal(0);
  readonly notesPage = signal(1);
  readonly notesTimezone = signal('UTC');
  readonly notesLoadingMore = signal(false);
  readonly hasMoreNotes = computed(() => this.notesPage() * NOTES_PAGE_SIZE < this.notesTotal());
  readonly noteText = signal('');
  readonly noteError = signal<string | null>(null);
  readonly addingNote = signal(false);

  readonly branches = signal<readonly BranchOption[]>([]);
  readonly countryCode = signal<string | null>(null);
  readonly tags = signal<readonly CustomerTag[]>([]);
  readonly tagsLoading = signal(false);

  readonly customerDrawerOpen = signal(false);
  readonly customerDrawerFocus = signal<CustomerFieldKey | null>(null);
  readonly propertyDrawerOpen = signal(false);
  readonly propertyDrawerMode = signal<PropertyDrawerMode>('create');
  readonly propertyDrawerId = signal<string | null>(null);
  readonly newRequestOpen = signal(false);
  readonly lockedCustomer = signal<CustomerOption | null>(null);
  readonly requestOptions = signal<RequestOptions | null>(null);
  readonly openingRequest = signal(false);

  readonly menuModel = signal<MenuItem[]>([]);
  readonly mutating = signal(false);
  readonly propertyBusyId = signal<string | null>(null);

  readonly header = computed(() => {
    const o = this.overview().data;
    if (o === null) {
      return null;
    }
    const phone = o.contact.phone?.trim() ? o.contact.phone : null;
    return {
      initials: initials(o.type, o.displayName, o.contact),
      name: o.displayName,
      typeLabel:
        o.type === 'commercial'
          ? t('customers.customerDetail.commercial')
          : t('customers.customerDetail.residential'),
      statusLabel: STATUS_LABELS[o.displayStatus] ?? o.displayStatus,
      severity: statusSeverity(o.displayStatus),
      email: o.contact.email,
      phone: phone === null ? null : formatPhone(phone),
      phoneHref: phone === null ? null : `tel:${phone.replace(/[^\d+]/g, '')}`,
      preferred: preferredContact(o.contact),
      balance:
        o.outstandingBalance === null || o.outstandingBalance === undefined
          ? null
          : formatMoney(o.outstandingBalance, o.currency),
      isActive: o.isActive,
    };
  });
  readonly activityGroups = computed(() => {
    const data = this.activity().data;
    return data === null ? [] : groupActivity(data.items, data.currency, data.timezone);
  });
  readonly canCreateRequest = computed(
    () => this.canMutate() && this.overview().data?.isActive === true,
  );

  private readonly requests = new Map<string, Subscription>();
  private optionsRequested = false;
  /** The drawer already asked to discard before "View existing customer" navigates. */
  private leaveConfirmed = false;

  /** An unknown tab, or one the role cannot read, shows Overview (BR-03). */
  private tabFor(code: string | null): DetailTab {
    return this.visibleTabs().find((item) => item.code === code)?.code ?? 'overview';
  }

  constructor() {
    // The deep-linked tab is known before the customer loads, so only its data is requested.
    this.tab.set(this.tabFor(this.route.snapshot.queryParamMap.get('tab')));
    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => this.openCustomer(params.get('customerId')));
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((query) => {
      this.tab.set(this.tabFor(query.get('tab')));
      this.loadTabData();
    });
    this.destroyRef.onDestroy(() => this.requests.forEach((request) => request.unsubscribe()));
  }

  // Loading

  private openCustomer(id: string | null): void {
    this.requests.forEach((request) => request.unsubscribe());
    this.requests.clear();
    this.overview.set(IDLE_REGION);
    this.properties.set(IDLE_REGION);
    this.recentWork.set(IDLE_REGION);
    this.appointments.set(IDLE_REGION);
    this.activity.set(IDLE_REGION);
    this.activityType.set('all');
    this.activityPeriod.set('last_90_days');
    this.activityTypes.set([]);
    this.loadingOlder.set(false);
    this.olderError.set(false);
    this.visitedKinds.set(new Set());
    this.notes.set(IDLE_REGION);
    this.noteText.set('');
    this.noteError.set(null);
    this.customerId.set(id);
    this.notFound.set(id === null || !UUID.test(id));
    if (this.forbidden() || this.notFound()) {
      return;
    }
    this.loadOverview();
    this.loadOptions();
    this.loadTabData();
  }

  /** Overview regions, Activity and each related list load the first time their tab is shown. */
  private loadTabData(): void {
    if (this.forbidden() || this.notFound() || this.customerId() === null) {
      return;
    }
    if (this.tab() === 'overview') {
      if (this.properties().status === 'idle') {
        this.loadProperties();
      }
      if (this.recentWork().status === 'idle') {
        this.loadRecentWork();
      }
      if (this.canReadJobs() && this.appointments().status === 'idle') {
        this.loadAppointments();
      }
      if (this.notes().status === 'idle') {
        this.loadNotes();
      }
    } else if (this.tab() === 'activity') {
      if (this.activity().status === 'idle') {
        this.loadActivity();
      }
    } else if (!this.visitedKinds().has(this.tab() as RelatedKind)) {
      // The related list loads itself when it first renders (BR-03).
      this.visitedKinds.update((kinds) => new Set(kinds).add(this.tab() as RelatedKind));
    }
  }

  private loadOptions(): void {
    if (this.optionsRequested || !this.canMutate()) {
      return;
    }
    this.optionsRequested = true;
    this.customers
      .branchOptions()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.branches.set(
            response.branches.map((branch) => ({ id: branch.id, name: branch.name })),
          );
          this.countryCode.set(response.countryCode);
        },
        error: (error: unknown) => this.handleFailure(error),
      });
    this.tagsLoading.set(true);
    this.customers
      .tags()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (tags) => {
          this.tagsLoading.set(false);
          this.tags.set(tags);
        },
        error: (error: unknown) => {
          this.tagsLoading.set(false);
          this.handleFailure(error);
        },
      });
  }

  /** `true` when the failure was a 401/403/404 the page resolves itself. */
  private handleFailure(error: unknown): boolean {
    const kind = isApiError(error) ? error.kind : null;
    if (kind === 'unauthorized') {
      this.onUnauthorized();
      return true;
    }
    if (kind === 'forbidden') {
      this.serverForbidden.set(true);
      return true;
    }
    if (kind === 'not-found') {
      this.notFound.set(true);
      return true;
    }
    return false;
  }

  /** Loads one region; `quiet` keeps the current data on screen while it refreshes. */
  private load<T>(
    key: string,
    source: Observable<T>,
    target: WritableSignal<RegionState<T>>,
    quiet = false,
  ): void {
    this.requests.get(key)?.unsubscribe();
    if (!quiet || target().data === null) {
      target.set({ status: 'loading', data: null });
    }
    this.requests.set(
      key,
      source.subscribe({
        next: (data) => target.set({ status: 'ready', data }),
        error: (error: unknown) => {
          if (!this.handleFailure(error)) {
            target.set({ status: 'error', data: null });
          }
        },
      }),
    );
  }

  private requireId(): string {
    return this.customerId() as string;
  }

  loadOverview(quiet = false): void {
    this.load('overview', this.customers.overview(this.requireId()), this.overview, quiet);
  }

  loadProperties(quiet = false): void {
    this.load('properties', this.customers.properties(this.requireId()), this.properties, quiet);
  }

  loadRecentWork(): void {
    this.load('recent', this.customers.recentWork(this.requireId()), this.recentWork);
  }

  loadAppointments(): void {
    this.load(
      'appointments',
      this.customers.upcomingAppointments(this.requireId()),
      this.appointments,
    );
  }

  loadActivity(): void {
    this.requests.get('activity-older')?.unsubscribe();
    this.loadingOlder.set(false);
    this.olderError.set(false);
    this.focusEventId.set(null);
    this.requests.get('activity')?.unsubscribe();
    this.activity.set({ status: 'loading', data: null });
    this.requests.set(
      'activity',
      this.customers
        .activity(this.requireId(), { type: this.activityType(), period: this.activityPeriod() })
        .subscribe({
          next: (data) => {
            this.activityTypes.set(data.availableTypes);
            this.activity.set({ status: 'ready', data });
          },
          error: (error: unknown) => {
            if (!this.handleFailure(error)) {
              this.activity.set({ status: 'error', data: null });
            }
          },
        }),
    );
  }

  /** "Load older activity": appends the next block without duplicates (BR-12). */
  loadOlderActivity(): void {
    const current = this.activity().data;
    const cursor = current?.nextCursor;
    if (current === null || !cursor || this.loadingOlder()) {
      return;
    }
    this.loadingOlder.set(true);
    this.olderError.set(false);
    this.requests.get('activity-older')?.unsubscribe();
    this.requests.set(
      'activity-older',
      this.customers
        .activity(this.requireId(), {
          type: this.activityType(),
          period: this.activityPeriod(),
          before: cursor,
        })
        .subscribe({
          next: (block) => {
            const known = new Set(current.items.map((item) => item.id));
            const fresh = block.items.filter((item) => !known.has(item.id));
            this.loadingOlder.set(false);
            this.activityTypes.set(block.availableTypes);
            this.activity.set({
              status: 'ready',
              data: {
                ...current,
                items: [...current.items, ...fresh],
                nextCursor: block.nextCursor,
              },
            });
            this.focusEventId.set(fresh[0]?.id ?? null);
          },
          error: (error: unknown) => {
            this.loadingOlder.set(false);
            if (!this.handleFailure(error)) {
              this.olderError.set(true);
            }
          },
        }),
    );
  }

  onActivityType(type: ActivityType | 'all'): void {
    this.activityType.set(type);
    this.loadActivity();
  }

  onActivityPeriod(period: ActivityPeriod): void {
    this.activityPeriod.set(period);
    this.loadActivity();
  }

  /** Forgets Activity after a change elsewhere; a visible tab reloads it right away. */
  private invalidateActivity(): void {
    if (this.activity().status === 'idle') {
      return;
    }
    this.activity.set(IDLE_REGION);
    if (this.tab() === 'activity') {
      this.loadActivity();
    }
  }

  loadNotes(): void {
    this.requests.get('notes')?.unsubscribe();
    this.notes.set({ status: 'loading', data: null });
    this.requests.set(
      'notes',
      this.customers.notes(this.requireId(), 1).subscribe({
        next: (response) => {
          this.notes.set({ status: 'ready', data: response.items });
          this.notesTotal.set(response.totalCount);
          this.notesPage.set(1);
          this.notesTimezone.set(response.timezone);
        },
        error: (error: unknown) => {
          if (!this.handleFailure(error)) {
            this.notes.set({ status: 'error', data: null });
          }
        },
      }),
    );
  }

  /** After a customer or property change: header, properties and (when seen) Activity refresh. */
  private refreshAfterChange(overview: boolean): void {
    if (overview) {
      this.loadOverview(true);
    }
    this.loadProperties(true);
    this.invalidateActivity();
  }

  // Tabs (BR-01)

  selectTab(code: DetailTab): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab: code === 'overview' ? null : code },
      queryParamsHandling: 'merge',
    });
  }

  onTabKeydown(event: KeyboardEvent, index: number): void {
    const tabs = this.visibleTabs();
    const last = tabs.length - 1;
    const next =
      event.key === 'ArrowRight'
        ? (index + 1) % tabs.length
        : event.key === 'ArrowLeft'
          ? (index + last) % tabs.length
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? last
              : -1;
    if (next < 0) {
      return;
    }
    event.preventDefault();
    this.selectTab(tabs[next].code);
    queueMicrotask(() =>
      this.host.querySelectorAll<HTMLElement>('[role="tab"]').item(next)?.focus(),
    );
  }

  // Menus

  openCreateMenu(event: Event): void {
    this.menuModel.set([
      {
        label: t('customers.customerDetail.property'),
        icon: 'pi pi-home',
        command: () => this.openAddProperty(),
      },
      ...CREATE_LINKS.filter((item) => item.path !== null || this.canCreateRequest()).map(
        (item) => ({
          label: item.label,
          command: () =>
            item.path === null ? this.openNewRequest() : void this.router.navigateByUrl(item.path),
        }),
      ),
    ]);
    this.menu().toggle(event);
  }

  openMoreMenu(event: Event): void {
    const o = this.overview().data;
    if (o === null) {
      return;
    }
    this.menuModel.set([
      o.isActive
        ? {
            label: t('customers.customerDetail.archiveCustomer'),
            icon: 'pi pi-box',
            disabled: this.mutating(),
            command: () => this.confirmArchiveCustomer(o),
          }
        : {
            label: t('customers.customerDetail.reactivateCustomer'),
            icon: 'pi pi-replay',
            disabled: this.mutating(),
            command: () => this.reactivateCustomer(o),
          },
    ]);
    this.menu().toggle(event);
  }

  openPropertyMenu(event: Event, property: PropertyItem): void {
    const busy = this.propertyBusyId() !== null;
    this.menuModel.set(
      property.isActive
        ? [
            {
              label: t('customers.customerDetail.setAsPrimary'),
              icon: 'pi pi-star',
              disabled: busy,
              command: () => this.runPropertyAction(property, 'set-primary'),
            },
            {
              label: t('customers.customerDetail.archive'),
              icon: 'pi pi-box',
              disabled: busy,
              command: () => this.confirmArchiveProperty(property),
            },
          ]
        : [
            {
              label: t('customers.customerDetail.reactivate'),
              icon: 'pi pi-replay',
              disabled: busy,
              command: () => this.runPropertyAction(property, 'reactivate'),
            },
          ],
    );
    this.menu().toggle(event);
  }

  // Drawers (only one open at a time)

  openEditCustomer(focus: CustomerFieldKey | null = null): void {
    this.showExclusive(() => {
      this.propertyDrawerOpen.set(false);
      this.newRequestOpen.set(false);
      this.customerDrawerFocus.set(focus);
      this.customerDrawerOpen.set(true);
    });
  }

  openAddProperty(): void {
    this.openPropertyDrawer('create', null);
  }

  openPropertyDrawer(mode: PropertyDrawerMode, id: string | null): void {
    this.showExclusive(() => {
      this.customerDrawerOpen.set(false);
      this.newRequestOpen.set(false);
      this.propertyDrawerMode.set(mode);
      this.propertyDrawerId.set(id);
      this.propertyDrawerOpen.set(true);
    });
  }

  viewProperty(property: PropertyItem): void {
    this.openPropertyDrawer('view', property.id);
  }

  editProperty(property: PropertyItem): void {
    this.openPropertyDrawer(this.canMutate() ? 'edit' : 'view', property.id);
  }

  /** Switching away from a drawer with unsaved edits asks first. */
  private showExclusive(show: () => void): void {
    const subject = this.dirtySubject();
    if (subject === null) {
      show();
      return;
    }
    this.confirmDiscard(subject).subscribe((leave) => {
      if (leave) {
        show();
      }
    });
  }

  private dirtySubject(): string | null {
    if (this.customerDrawer()?.isDirty()) {
      return t('customers.customerDetail.thisCustomer');
    }
    if (this.propertyDrawer()?.isDirty()) {
      return t('customers.customerDetail.thisProperty');
    }
    return this.newRequestOpen() && this.requestDrawer()?.dirty()
      ? t('requests.newRequestDrawer.thisRequest')
      : null;
  }

  // New request (BR-09): the existing drawer opens in place with the customer locked.

  openNewRequest(): void {
    const id = this.customerId();
    if (id === null || !this.canCreateRequest() || this.openingRequest()) {
      return;
    }
    this.openingRequest.set(true);
    this.requestsService
      .customerOptions('', id)
      .pipe(
        switchMap((response) => {
          const customer = response.items[0] ?? null;
          if (customer === null || this.requestOptions() !== null) {
            return of({ customer, options: this.requestOptions() });
          }
          return this.requestsService.options().pipe(map((options) => ({ customer, options })));
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ customer, options }) => {
          this.openingRequest.set(false);
          if (customer === null) {
            this.messageService.add({
              severity: 'error',
              summary: t('customers.customerDetail.requestUnavailableMessage'),
            });
            return;
          }
          this.requestOptions.set(options);
          this.showExclusive(() => {
            this.customerDrawerOpen.set(false);
            this.propertyDrawerOpen.set(false);
            this.lockedCustomer.set(customer);
            this.newRequestOpen.set(true);
          });
        },
        error: (error: unknown) => {
          this.openingRequest.set(false);
          if (!this.handleFailure(error)) {
            this.messageService.add({
              severity: 'error',
              summary: t('customers.customerDetail.requestOpenFailedMessage'),
            });
          }
        },
      });
  }

  onNewRequestClosed(): void {
    this.newRequestOpen.set(false);
  }

  /** The request exists: no navigation; the Requests tab, Overview work and Activity refresh. */
  onRequestCreated(): void {
    this.newRequestOpen.set(false);
    this.messageService.add({
      severity: 'success',
      summary: t('requests.requests.requestCreated'),
    });
    this.relatedLists()
      .find((list) => list.kind() === 'requests')
      ?.load();
    this.recentWork.set(IDLE_REGION);
    this.loadTabData();
    this.invalidateActivity();
  }

  onCustomerDrawerClosed(): void {
    this.customerDrawerOpen.set(false);
    this.customerDrawerFocus.set(null);
  }

  onCustomerSaved(): void {
    this.refreshAfterChange(true);
  }

  onPropertyDrawerClosed(): void {
    this.propertyDrawerOpen.set(false);
  }

  onPropertySaved(): void {
    this.refreshAfterChange(false);
  }

  onTagCreated(tag: CustomerTag): void {
    if (!this.tags().some((existing) => existing.id === tag.id)) {
      this.tags.update((tags) =>
        [...tags, tag].sort((a, b) =>
          a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
        ),
      );
    }
  }

  onCustomerUnavailable(): void {
    this.messageService.add({ severity: 'error', summary: CUSTOMER_UNAVAILABLE_MESSAGE() });
    this.leaveConfirmed = true;
    void this.router.navigate(['/customers']).finally(() => (this.leaveConfirmed = false));
  }

  /** `404` from the property drawer: a missing property (edit/view) or customer (create). */
  onPropertyUnavailable(): void {
    if (this.propertyDrawerMode() === 'create') {
      this.onCustomerUnavailable();
      return;
    }
    this.messageService.add({ severity: 'error', summary: PROPERTY_UNAVAILABLE_MESSAGE() });
    this.propertyDrawerOpen.set(false);
    this.refreshAfterChange(false);
  }

  /** "View existing customer": the drawer already confirmed any discard (BR-19). */
  onOpenExisting(id: string): void {
    this.leaveConfirmed = true;
    void this.router.navigate(['/customers', id]).finally(() => (this.leaveConfirmed = false));
  }

  /** Consulted by `customerDetailUnsavedChangesGuard` on route leave. */
  canLeave(): boolean | Observable<boolean> {
    if (this.sessionExpired() || this.leaveConfirmed) {
      return true;
    }
    const subject = this.dirtySubject();
    return subject === null ? true : this.confirmDiscard(subject);
  }

  private confirmDiscard(subject: string): Observable<boolean> {
    return new Observable<boolean>((subscriber) => {
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject,
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

  /** 401/403/404 from a related list: the page resolves it like any other region. */
  onRelatedFailure(error: unknown): void {
    this.handleFailure(error);
  }

  onUnauthorized(): void {
    handleUnauthorized(this.router, this.sessionExpired);
  }

  // Portal access (customer-portal-dashboard BR-10, BR-11, BR-14)

  invitePortal(): void {
    const id = this.customerId();
    if (id !== null) {
      this.changePortalAccess(this.customers.invitePortal(id));
    }
  }

  confirmRemovePortal(): void {
    const customer = this.overview().data;
    if (customer === null) {
      return;
    }
    this.confirmationService.confirm({
      header: t('customers.customerDetail.removePortalAccess'),
      message: t('customers.customerDetail.willBeSignedOut', { displayName: customer.displayName }),
      defaultFocus: 'reject',
      acceptButtonProps: {
        label: t('customers.customerDetail.removePortalAccess2'),
        severity: 'danger',
      },
      rejectButtonProps: { label: t('common.cancel'), severity: 'secondary', outlined: true },
      accept: () => this.changePortalAccess(this.customers.removePortalAccess(customer.id)),
    });
  }

  private changePortalAccess(request$: Observable<PortalAccess>): void {
    if (this.portalBusy()) {
      return;
    }
    this.portalBusy.set(true);
    this.portalError.set(null);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (access) => {
        this.portalBusy.set(false);
        this.overview.update((region) =>
          region.data === null
            ? region
            : {
                ...region,
                data: { ...region.data, contact: { ...region.data.contact, ...access } },
              },
        );
      },
      error: (error: unknown) => {
        this.portalBusy.set(false);
        const apiError = isApiError(error) ? error : null;
        if (apiError?.kind === 'unauthorized') {
          this.onUnauthorized();
        } else if (apiError?.status === 409 && apiError.code === 'portal_invite_unavailable') {
          this.portalError.set(PORTAL_INVITE_UNAVAILABLE_MESSAGE());
        } else {
          this.portalError.set(PORTAL_FAILED_MESSAGE());
        }
      },
    });
  }

  // Customer archive / reactivate (BR-10)

  private confirmArchiveCustomer(customer: CustomerOverview): void {
    this.confirmationService.confirm({
      header: t('customers.customerDetail.archive2', { displayName: customer.displayName }),
      message: t('customers.customerDetail.archivedCustomersAreHidden'),
      defaultFocus: 'reject',
      acceptButtonProps: { label: t('customers.customerDetail.archive') },
      rejectButtonProps: { label: t('common.cancel'), severity: 'secondary', outlined: true },
      accept: () =>
        this.mutateCustomer(
          this.customers.archive(customer.id),
          t('customers.customerDetail.customerArchived'),
          ALREADY_ARCHIVED_MESSAGE(),
        ),
    });
  }

  private reactivateCustomer(customer: CustomerOverview): void {
    this.mutateCustomer(
      this.customers.reactivate(customer.id),
      t('customers.customerDetail.customerReactivated'),
      ALREADY_ACTIVE_MESSAGE(),
    );
  }

  private mutateCustomer(request$: Observable<unknown>, success: string, conflict: string): void {
    if (this.mutating()) {
      return;
    }
    this.mutating.set(true);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.mutating.set(false);
        this.messageService.add({ severity: 'success', summary: success });
        this.refreshAfterChange(true);
      },
      error: (error: unknown) => {
        this.mutating.set(false);
        const kind = isApiError(error) ? error.kind : null;
        if (kind === 'unauthorized') {
          this.onUnauthorized();
        } else if (kind === 'conflict') {
          this.messageService.add({ severity: 'error', summary: conflict });
          this.loadOverview(true);
        } else if (kind === 'not-found') {
          this.onCustomerUnavailable();
        } else {
          this.messageService.add({ severity: 'error', summary: UPDATE_FAILED_MESSAGE() });
        }
      },
    });
  }

  // Property actions (BR-07)

  private confirmArchiveProperty(property: PropertyItem): void {
    this.confirmationService.confirm({
      header: t('customers.customerDetail.archive3', { name: property.name }),
      message: t('customers.customerDetail.archivedPropertiesAreHidden'),
      defaultFocus: 'reject',
      acceptButtonProps: { label: t('customers.customerDetail.archive') },
      rejectButtonProps: { label: t('common.cancel'), severity: 'secondary', outlined: true },
      accept: () => this.runPropertyAction(property, 'archive'),
    });
  }

  /** BR-07 message for a `409`, chosen from the action and the property as the user saw it. */
  private conflictMessage(property: PropertyItem, action: PropertyAction): string {
    switch (action) {
      case 'set-primary':
        return property.isPrimary
          ? t('customers.customerDetail.thisPropertyIsAlready')
          : !property.isActive
            ? t('customers.customerDetail.reactivateThisPropertyBefore')
            : t('customers.customerDetail.thePrimaryPropertyWas');
      case 'archive':
        return property.isActive
          ? t('customers.customerDetail.setAnotherPropertyAs')
          : t('customers.customerDetail.thisPropertyIsAlready2');
      case 'reactivate':
        return t('customers.customerDetail.thisPropertyIsAlready3');
    }
  }

  private runPropertyAction(property: PropertyItem, action: PropertyAction): void {
    if (this.propertyBusyId() !== null) {
      return;
    }
    const id = this.requireId();
    const request$ =
      action === 'set-primary'
        ? this.customers.setPrimaryProperty(id, property.id)
        : action === 'archive'
          ? this.customers.archiveProperty(id, property.id)
          : this.customers.reactivateProperty(id, property.id);
    const success =
      action === 'set-primary'
        ? t('customers.customerDetail.primaryPropertyUpdated')
        : action === 'archive'
          ? t('customers.customerDetail.propertyArchived')
          : t('customers.customerDetail.propertyReactivated');
    this.propertyBusyId.set(property.id);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.propertyBusyId.set(null);
        this.messageService.add({ severity: 'success', summary: success });
        this.refreshAfterChange(false);
      },
      error: (error: unknown) => {
        this.propertyBusyId.set(null);
        const kind = isApiError(error) ? error.kind : null;
        if (kind === 'unauthorized') {
          this.onUnauthorized();
        } else if (kind === 'conflict') {
          this.messageService.add({
            severity: 'error',
            summary: this.conflictMessage(property, action),
          });
          this.refreshAfterChange(false);
        } else if (kind === 'not-found') {
          this.messageService.add({ severity: 'error', summary: PROPERTY_UNAVAILABLE_MESSAGE() });
          this.refreshAfterChange(false);
        } else {
          this.messageService.add({ severity: 'error', summary: PROPERTY_UPDATE_FAILED_MESSAGE() });
        }
      },
    });
  }

  // Notes (BR-16)

  onNoteText(value: string): void {
    this.noteText.set(value);
    if (this.noteError() !== null) {
      this.noteError.set(null);
    }
  }

  addNote(): void {
    if (this.addingNote()) {
      return;
    }
    const invalid = validateNote(this.noteText());
    if (invalid !== null) {
      this.noteError.set(invalid);
      return;
    }
    this.addingNote.set(true);
    this.customers
      .addNote(this.requireId(), this.noteText().trim())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (entry) => {
          this.addingNote.set(false);
          this.noteText.set('');
          this.noteError.set(null);
          this.notesTotal.update((total) => total + 1);
          this.notes.update((state) =>
            state.status === 'ready'
              ? { status: 'ready', data: [entry, ...(state.data ?? [])] }
              : state,
          );
          this.messageService.add({
            severity: 'success',
            summary: t('customers.customerDetail.noteAdded'),
          });
          this.invalidateActivity();
        },
        error: (error: unknown) => {
          this.addingNote.set(false);
          const apiError = isApiError(error) ? error : null;
          if (apiError?.kind === 'unauthorized') {
            this.onUnauthorized();
          } else if (apiError?.kind === 'not-found') {
            this.onCustomerUnavailable();
          } else if (
            (apiError?.kind === 'validation' || apiError?.kind === 'bad-request') &&
            Object.values(apiError.fieldErrors).some((messages) => messages.length > 0)
          ) {
            const first = Object.values(apiError.fieldErrors).find(
              (messages) => messages.length > 0,
            );
            this.noteError.set(first?.[0] ?? NOTE_FAILED_MESSAGE());
          } else {
            this.messageService.add({ severity: 'error', summary: NOTE_FAILED_MESSAGE() });
          }
        },
      });
  }

  showMoreNotes(): void {
    if (this.notesLoadingMore()) {
      return;
    }
    const page = this.notesPage() + 1;
    this.notesLoadingMore.set(true);
    this.customers
      .notes(this.requireId(), page)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.notesLoadingMore.set(false);
          this.notesPage.set(page);
          this.notesTotal.set(response.totalCount);
          this.notes.update((state) => {
            const known = new Set((state.data ?? []).map((note) => note.id));
            return {
              status: 'ready',
              data: [
                ...(state.data ?? []),
                ...response.items.filter((note) => !known.has(note.id)),
              ],
            };
          });
        },
        error: (error: unknown) => {
          this.notesLoadingMore.set(false);
          if (!this.handleFailure(error)) {
            this.messageService.add({
              severity: 'error',
              summary: t('customers.customerDetail.weCouldntLoadNotes'),
            });
          }
        },
      });
  }
}
