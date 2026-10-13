import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
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
import { Observable, Subscription } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
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
import { SideCards } from '../../components/side-cards/side-cards';
import {
  ACTIVITY_PAGE_SIZE,
  ActivityResponse,
  AppointmentsResponse,
  CustomerFieldKey,
  CustomerOverview,
  CustomerTag,
  DETAIL_TABS,
  DetailTab,
  IDLE_REGION,
  NOTES_PAGE_SIZE,
  NoteItem,
  PropertiesResponse,
  PortalAccess,
  PropertyItem,
  RecentWorkResponse,
  RegionState,
} from '../../models/customer.model';
import { CustomersService } from '../../services/customers.service';
import {
  activityLabel,
  formatDateTime,
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

export const NOT_FOUND_TITLE = 'Customer not found';
export const NOT_FOUND_MESSAGE = "This customer doesn't exist or you don't have access to it.";
export const DETAIL_ERROR_MESSAGE = "We couldn't load this customer.";
export const PROPERTY_UPDATE_FAILED_MESSAGE = "We couldn't update the property. Try again.";
export const NOTE_FAILED_MESSAGE = "We couldn't add the note. Try again.";
export const PORTAL_FAILED_MESSAGE = "We couldn't update portal access. Try again.";
export const PORTAL_INVITE_UNAVAILABLE_MESSAGE = "Portal access can't be sent for this contact.";
const MUTATE_ROLES: readonly string[] = ['owner', 'dispatcher'];
const READ_ROLES: readonly string[] = ['operations_manager', 'accounting', 'viewer'];
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

interface TabInfo {
  readonly code: DetailTab;
  readonly label: string;
  /** Coming soon destination of the transactional tabs (BR-18). */
  readonly soon?: { readonly noun: string; readonly link: string };
}

const TABS: readonly TabInfo[] = [
  { code: 'overview', label: 'Overview' },
  {
    code: 'requests',
    label: 'Requests',
    soon: { noun: 'requests', link: '/requests' },
  },
  { code: 'quotes', label: 'Quotes', soon: { noun: 'quotes', link: '/coming-soon/quotes' } },
  { code: 'jobs', label: 'Jobs', soon: { noun: 'jobs', link: '/coming-soon/work-orders' } },
  {
    code: 'invoices',
    label: 'Invoices',
    soon: { noun: 'invoices', link: '/coming-soon/invoices' },
  },
  { code: 'activity', label: 'Activity' },
];

const CREATE_LINKS: readonly { readonly label: string; readonly path: string }[] = [
  { label: 'Request', path: '/requests' },
  { label: 'Quote', path: '/coming-soon/quotes' },
  { label: 'Job', path: '/coming-soon/work-orders' },
  { label: 'Invoice', path: '/coming-soon/invoices' },
];

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
    RouterLink,
    ButtonDirective,
    Menu,
    Message,
    Skeleton,
    Tag,
    Toast,
    ConfirmDialog,
    DiscardChangesDialog,
    AppointmentsCard,
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
  private readonly destroyRef = inject(DestroyRef);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  private readonly customerDrawer = viewChild(CustomerDrawer);
  private readonly propertyDrawer = viewChild(PropertyDrawer);
  private readonly menu = viewChild.required(Menu);

  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly notFoundTitle = NOT_FOUND_TITLE;
  readonly notFoundMessage = NOT_FOUND_MESSAGE;
  readonly detailErrorMessage = DETAIL_ERROR_MESSAGE;
  readonly tabs = TABS;
  readonly activityPageSize = ACTIVITY_PAGE_SIZE;

  private readonly roleCode = computed(() => this.sessionService.session()?.role.code ?? '');
  readonly canMutate = computed(() => MUTATE_ROLES.includes(this.roleCode()));
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
  readonly activityPage = signal(1);

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
      typeLabel: o.type === 'commercial' ? 'Commercial' : 'Residential',
      statusLabel: STATUS_LABELS[o.displayStatus] ?? o.displayStatus,
      severity: statusSeverity(o.displayStatus),
      email: o.contact.email,
      phone: phone === null ? null : formatPhone(phone),
      phoneHref: phone === null ? null : `tel:${phone.replace(/[^\d+]/g, '')}`,
      preferred: preferredContact(o.contact),
      balance: formatMoney(o.outstandingBalance, o.currency),
      isActive: o.isActive,
    };
  });
  readonly activityEntries = computed(() => {
    const data = this.activity().data;
    return (data?.items ?? []).map((item) => ({
      id: item.id,
      label: activityLabel(item.action, item.subjectName),
      actor: item.actorName?.trim() || 'System',
      date: formatDateTime(item.occurredAt, data?.timezone ?? 'UTC'),
    }));
  });
  readonly activityPages = computed(() =>
    Math.max(1, Math.ceil((this.activity().data?.totalCount ?? 0) / ACTIVITY_PAGE_SIZE)),
  );
  readonly activeTab = computed(() => TABS.find((item) => item.code === this.tab()) ?? TABS[0]);

  private readonly requests = new Map<string, Subscription>();
  private optionsRequested = false;
  /** The drawer already asked to discard before "View existing customer" navigates. */
  private leaveConfirmed = false;

  constructor() {
    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => this.openCustomer(params.get('customerId')));
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((query) => {
      const requested = DETAIL_TABS.find((code) => code === query.get('tab'));
      this.tab.set(requested ?? 'overview');
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
    this.notes.set(IDLE_REGION);
    this.activityPage.set(1);
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

  /** Overview regions and Activity load on first view; transactional tabs make no request. */
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
      if (this.appointments().status === 'idle') {
        this.loadAppointments();
      }
      if (this.notes().status === 'idle') {
        this.loadNotes();
      }
    } else if (this.tab() === 'activity' && this.activity().status === 'idle') {
      this.loadActivity(1);
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

  loadActivity(page: number): void {
    this.activityPage.set(page);
    this.load('activity', this.customers.activity(this.requireId(), page), this.activity);
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
    if (this.activity().status === 'idle') {
      return;
    }
    if (this.tab() === 'activity') {
      this.loadActivity(1);
    } else {
      this.activity.set(IDLE_REGION);
    }
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
    const last = TABS.length - 1;
    const next =
      event.key === 'ArrowRight'
        ? (index + 1) % TABS.length
        : event.key === 'ArrowLeft'
          ? (index + last) % TABS.length
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? last
              : -1;
    if (next < 0) {
      return;
    }
    event.preventDefault();
    this.selectTab(TABS[next].code);
    queueMicrotask(() =>
      this.host.querySelectorAll<HTMLElement>('[role="tab"]').item(next)?.focus(),
    );
  }

  // Menus

  openCreateMenu(event: Event): void {
    this.menuModel.set([
      { label: 'Property', icon: 'pi pi-home', command: () => this.openAddProperty() },
      ...CREATE_LINKS.map((item) => ({
        label: item.label,
        command: () => void this.router.navigateByUrl(item.path),
      })),
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
            label: 'Archive customer',
            icon: 'pi pi-box',
            disabled: this.mutating(),
            command: () => this.confirmArchiveCustomer(o),
          }
        : {
            label: 'Reactivate customer',
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
              label: 'Set as primary',
              icon: 'pi pi-star',
              disabled: busy,
              command: () => this.runPropertyAction(property, 'set-primary'),
            },
            {
              label: 'Archive',
              icon: 'pi pi-box',
              disabled: busy,
              command: () => this.confirmArchiveProperty(property),
            },
          ]
        : [
            {
              label: 'Reactivate',
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
      return 'this customer';
    }
    return this.propertyDrawer()?.isDirty() ? 'this property' : null;
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
    this.messageService.add({ severity: 'error', summary: CUSTOMER_UNAVAILABLE_MESSAGE });
    this.leaveConfirmed = true;
    void this.router.navigate(['/customers']).finally(() => (this.leaveConfirmed = false));
  }

  /** `404` from the property drawer: a missing property (edit/view) or customer (create). */
  onPropertyUnavailable(): void {
    if (this.propertyDrawerMode() === 'create') {
      this.onCustomerUnavailable();
      return;
    }
    this.messageService.add({ severity: 'error', summary: PROPERTY_UNAVAILABLE_MESSAGE });
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
      header: 'Remove portal access?',
      message: `${customer.displayName} will be signed out of the portal.`,
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Remove portal access', severity: 'danger' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
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
          this.portalError.set(PORTAL_INVITE_UNAVAILABLE_MESSAGE);
        } else {
          this.portalError.set(PORTAL_FAILED_MESSAGE);
        }
      },
    });
  }

  // Customer archive / reactivate (BR-10)

  private confirmArchiveCustomer(customer: CustomerOverview): void {
    this.confirmationService.confirm({
      header: `Archive ${customer.displayName}?`,
      message:
        'Archived customers are hidden from active lists. Their contacts, properties and history are kept.',
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Archive' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () =>
        this.mutateCustomer(
          this.customers.archive(customer.id),
          'Customer archived.',
          ALREADY_ARCHIVED_MESSAGE,
        ),
    });
  }

  private reactivateCustomer(customer: CustomerOverview): void {
    this.mutateCustomer(
      this.customers.reactivate(customer.id),
      'Customer reactivated.',
      ALREADY_ACTIVE_MESSAGE,
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
          this.messageService.add({ severity: 'error', summary: UPDATE_FAILED_MESSAGE });
        }
      },
    });
  }

  // Property actions (BR-07)

  private confirmArchiveProperty(property: PropertyItem): void {
    this.confirmationService.confirm({
      header: `Archive ${property.name}?`,
      message:
        "Archived properties are hidden from this customer's active properties. Their history is kept.",
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Archive' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () => this.runPropertyAction(property, 'archive'),
    });
  }

  /** BR-07 message for a `409`, chosen from the action and the property as the user saw it. */
  private conflictMessage(property: PropertyItem, action: PropertyAction): string {
    switch (action) {
      case 'set-primary':
        return property.isPrimary
          ? 'This property is already the primary property.'
          : !property.isActive
            ? 'Reactivate this property before making it primary.'
            : 'The primary property was changed by someone else. Refresh and try again.';
      case 'archive':
        return property.isActive
          ? 'Set another property as primary before archiving this one.'
          : 'This property is already archived.';
      case 'reactivate':
        return 'This property is already active.';
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
        ? 'Primary property updated.'
        : action === 'archive'
          ? 'Property archived.'
          : 'Property reactivated.';
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
          this.messageService.add({ severity: 'error', summary: PROPERTY_UNAVAILABLE_MESSAGE });
          this.refreshAfterChange(false);
        } else {
          this.messageService.add({ severity: 'error', summary: PROPERTY_UPDATE_FAILED_MESSAGE });
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
          this.messageService.add({ severity: 'success', summary: 'Note added.' });
          if (this.activity().status !== 'idle') {
            this.activity.set(IDLE_REGION);
            if (this.tab() === 'activity') {
              this.loadActivity(1);
            }
          }
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
            this.noteError.set(first?.[0] ?? NOTE_FAILED_MESSAGE);
          } else {
            this.messageService.add({ severity: 'error', summary: NOTE_FAILED_MESSAGE });
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
            this.messageService.add({ severity: 'error', summary: "We couldn't load notes." });
          }
        },
      });
  }
}
