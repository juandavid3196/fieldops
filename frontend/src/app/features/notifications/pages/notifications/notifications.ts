import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  linkedSignal,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MenuItem, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Menu } from 'primeng/menu';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import {
  EMPTY,
  Observable,
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  switchMap,
  tap,
} from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { isApiError } from '../../../../core/models/api-error.model';
import { NotificationUnreadService } from '../../../../core/services/notification-unread.service';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import {
  NOTIFICATIONS_EMPTY_TEXT,
  NOTIFICATIONS_EMPTY_TITLE,
  NOTIFICATIONS_FILTERED_EMPTY_MESSAGE,
  NOTIFICATIONS_FORBIDDEN_MESSAGE,
  NOTIFICATIONS_LOAD_ERROR_MESSAGE,
  NOTIFICATIONS_PAGE_SIZES,
  NOTIFICATION_TARGET_GONE_MESSAGE,
  NOTIFICATION_UNAVAILABLE_MESSAGE,
  NOTIFICATION_UPDATE_FAILED_MESSAGE,
  NotificationItem,
  NotificationsOptions,
  NotificationsPage,
  NotificationsRequest,
  NotificationsSummary,
  NotificationsTab,
  NotificationsView,
} from '../../models/notifications.model';
import { NotificationsService } from '../../services/notifications.service';
import {
  IconTone,
  NOTIFICATION_DATES,
  NOTIFICATION_TABS,
  NOTIFICATION_TYPES,
  activeFilterCount,
  fullDateTime,
  iconFor,
  isCanonicalNotificationsUrl,
  isToday,
  notificationsViewParams,
  relativeTime,
  resolveNotificationsView,
  targetRoute,
} from '../../utils/notifications-format';

import { localizedList, t } from '../../../../core/i18n/translate';
import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';
import { NotificationTextPipe } from '../../utils/notification-text.pipe';
interface Region<T> {
  readonly status: 'idle' | 'loading' | 'ready' | 'error';
  readonly data: T | null;
}

interface Choice {
  readonly code: string;
  readonly label: string;
}

interface RowView {
  readonly item: NotificationItem;
  readonly icon: string;
  readonly tone: IconTone;
  readonly relative: string;
  readonly full: string;
  readonly name: string;
  readonly detail: string;
  readonly route: { commands: string[]; queryParams: Record<string, string> | null } | null;
}

interface GroupView {
  readonly label: string;
  readonly rows: readonly RowView[];
}

const IDLE = { status: 'idle', data: null } as const;
const ALL = 'all';
const SEARCH_DEBOUNCE_MS = 300;
const FIELD_PARAMS = ['tab', 'q', 'type', 'date', 'page', 'pageSize'];

/**
 * Notifications center (`/notifications`, Design 32): cards, tabs with counts, filters and a
 * server-paged list grouped by day. The URL holds the view (BR-20); invalid values are normalized
 * with `replaceUrl` before any request. The first request is the options, so a `403` stops the rest.
 */
@Component({
  selector: 'app-notifications',
  imports: [
    NotificationTextPipe,
    LocalizedRefPipe,
    TranslocoPipe,
    ButtonDirective,
    FormsModule,
    IconField,
    InputIcon,
    InputText,
    Menu,
    Paginator,
    RouterLink,
    Select,
    Skeleton,
    Toast,
  ],
  providers: [MessageService],
  templateUrl: './notifications.html',
  styleUrl: './notifications.scss',
})
export class Notifications {
  private readonly api = inject(NotificationsService);
  private readonly unread = inject(NotificationUnreadService);
  private readonly messageService = inject(MessageService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  get forbiddenMessage(): string {
    return NOTIFICATIONS_FORBIDDEN_MESSAGE();
  }
  get errorMessage(): string {
    return NOTIFICATIONS_LOAD_ERROR_MESSAGE();
  }
  get emptyTitle(): string {
    return NOTIFICATIONS_EMPTY_TITLE();
  }
  get emptyText(): string {
    return NOTIFICATIONS_EMPTY_TEXT();
  }
  get filteredEmptyMessage(): string {
    return NOTIFICATIONS_FILTERED_EMPTY_MESSAGE();
  }
  readonly tabs = NOTIFICATION_TABS;
  readonly typeOptions: Choice[] = localizedList(() => [
    { code: ALL, label: t('notifications.notifications.allTypes') },
    ...NOTIFICATION_TYPES.map(({ code, label }) => ({ code, label })),
  ]);
  readonly dateOptions: Choice[] = NOTIFICATION_DATES.map(({ code, label }) => ({ code, label }));
  readonly pageSizeOptions: number[] = [...NOTIFICATIONS_PAGE_SIZES];
  readonly skeletonRows = [1, 2, 3, 4, 5];

  readonly sessionExpired = signal(false);
  readonly forbidden = signal(false);

  readonly options = signal<Region<NotificationsOptions>>(IDLE);
  readonly summary = signal<Region<NotificationsSummary>>(IDLE);
  readonly list = signal<Region<NotificationsPage>>(IDLE);
  private readonly now = signal(new Date());
  readonly rowMenu = signal<MenuItem[]>([]);
  readonly markingAll = signal(false);
  /** BR-25: a poll or tab-focus refresh found more unread than the page last received. */
  readonly newNotice = signal(false);
  private knownUnread: number | null = null;

  private readonly query = toSignal(this.route.queryParamMap, { requireSync: true });
  private readonly branches = computed(() => this.options().data?.branches ?? []);
  readonly view = computed<NotificationsView>(() =>
    resolveNotificationsView(
      (key) => this.query().get(key),
      this.options().data === null ? null : this.branches().map((branch) => branch.id),
    ),
  );
  private readonly canonical = computed(() =>
    isCanonicalNotificationsUrl((key) => this.query().get(key), this.view()),
  );

  readonly searchText = linkedSignal(() => this.view().q);
  readonly filterCount = computed(() => activeFilterCount(this.view()));
  readonly counts = computed(() => this.list().data?.counts ?? null);
  /** BR-21: the branch dropdown is hidden with fewer than two branches. */
  readonly canChooseBranch = computed(() => this.branches().length > 1);
  readonly branchChoices = computed<Choice[]>(() => [
    { code: ALL, label: t('notifications.notifications.allBranches') },
    ...this.branches().map((branch) => ({ code: branch.id, label: branch.name })),
  ]);
  readonly controlsDisabled = computed(() => this.options().status !== 'ready');

  readonly cards = computed(() => {
    const data = this.summary().data;
    const show = (value: number | undefined): string =>
      this.summary().status === 'error' || value === undefined ? '—' : String(value);
    return [
      {
        label: t('notifications.notifications.unread'),
        value: show(data?.unread),
        icon: 'pi-envelope',
      },
      {
        label: t('notifications.notifications.today'),
        value: show(data?.today),
        icon: 'pi-calendar',
      },
    ];
  });
  readonly markAllDisabled = computed(
    () =>
      this.markingAll() ||
      this.options().status !== 'ready' ||
      (this.summary().data?.unread ?? 1) === 0,
  );

  readonly total = computed(() => this.list().data?.total ?? 0);
  readonly announcement = computed(() =>
    this.list().status === 'ready'
      ? t('notifications.notifications.notifications2', { total: this.total() })
      : '',
  );
  readonly groups = computed<GroupView[]>(() => {
    const data = this.list().data;
    if (data === null) {
      return [];
    }
    const now = this.now();
    const zone = data.timeZone;
    const today: RowView[] = [];
    const earlier: RowView[] = [];
    for (const item of data.items) {
      const { icon, tone } = iconFor(item);
      const relative = relativeTime(item.createdAt, zone, now);
      const detail = [item.secondaryLabel, item.branch?.name]
        .filter((part): part is string => part !== null && part !== undefined && part !== '')
        .join(' · ');
      const row: RowView = {
        item,
        icon,
        tone,
        relative,
        full: fullDateTime(item.createdAt, zone),
        name: `${item.title}, ${item.isRead ? t('notifications.notifications.read') : t('notifications.notifications.unread2')}, ${relative}`,
        detail,
        route: item.target === null ? null : targetRoute(item.target.kind, item.target.id),
      };
      (isToday(item.createdAt, zone, now) ? today : earlier).push(row);
    }
    return [
      { label: t('notifications.notifications.today'), rows: today },
      { label: t('notifications.notifications.earlier'), rows: earlier },
    ].filter((group) => group.rows.length > 0);
  });
  /** BR-18/UI table: the inbox-empty copy shows only with no filters and no tab narrowing. */
  readonly inboxEmpty = computed(
    () => this.view().tab === 'all' && this.filterCount() === 0 && this.total() === 0,
  );
  readonly canReset = computed(() => this.filterCount() > 0 || this.view().tab !== 'all');
  readonly footer = computed(() => {
    const { page, pageSize } = this.view();
    const total = this.total();
    return t('notifications.notifications.of', {
      value: (page - 1) * pageSize + 1,
      value2: Math.min(page * pageSize, total),
      total: total,
    });
  });

  /** Request gates: null until the options are known and the URL is canonical. */
  private readonly listRequest = computed<NotificationsRequest | null>(
    () => {
      if (this.forbidden() || this.options().status !== 'ready' || !this.canonical()) {
        return null;
      }
      const view = this.view();
      return {
        tab: view.tab,
        q: view.q,
        type: view.type,
        branchId: view.branch,
        date: view.date,
        page: view.page,
        pageSize: view.pageSize,
      };
    },
    { equal: (a, b) => JSON.stringify(a) === JSON.stringify(b) },
  );

  private readonly options$ = new Subject<void>();
  private readonly summary$ = new Subject<void>();
  private readonly list$ = new Subject<NotificationsRequest>();
  private readonly searches$ = new Subject<string>();

  constructor() {
    this.options$
      .pipe(
        switchMap(() => this.fetch(this.options, this.api.options())),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.summary$
      .pipe(
        switchMap(() => this.fetch(this.summary, this.api.summary())),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.list$
      .pipe(
        switchMap((request) => {
          this.newNotice.set(false);
          return this.fetch(this.list, this.api.list(request)).pipe(
            tap((page) => {
              this.knownUnread = page.counts.unread;
              this.now.set(new Date());
              this.clampPage(page, request);
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.searches$
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((text) => {
        const term = text.trim();
        if (term !== this.view().q) {
          this.change({ q: term });
        }
      });

    // BR-20: invalid URL values are replaced by their defaults before any request.
    effect(() => {
      if (this.options().status === 'ready' && !this.canonical()) {
        const view = this.view();
        untracked(() => this.navigate(view, true));
      }
    });
    effect(() => {
      const request = this.listRequest();
      if (request !== null) {
        untracked(() => this.list$.next(request));
      }
    });
    // The summary loads once, after the options proved access.
    effect(() => {
      if (this.options().status === 'ready' && !this.forbidden()) {
        untracked(() => this.summary$.next());
      }
    });
    const seen = this.unread.polledSummary();
    effect(() => {
      const polled = this.unread.polledSummary();
      untracked(() => {
        if (
          polled !== null &&
          polled !== seen &&
          !this.forbidden() &&
          this.knownUnread !== null &&
          polled.unread > this.knownUnread
        ) {
          this.newNotice.set(true);
        }
      });
    });
    this.options$.next();
  }

  /** BR-25: reloads list and summary with the current URL state; the notice goes away. */
  refreshNew(): void {
    this.newNotice.set(false);
    this.reload();
  }

  /** Loads one region, keeping the previous data while reloading. */
  private fetch<T>(
    region: ReturnType<typeof signal<Region<T>>>,
    request: Observable<T>,
  ): Observable<T> {
    region.update((current) => ({ status: 'loading', data: current.data }));
    return request.pipe(
      tap((data) => region.set({ status: 'ready', data })),
      catchError((error: unknown) => {
        region.set({ status: 'error', data: null });
        this.handleFailure(error);
        return EMPTY;
      }),
    );
  }

  private handleFailure(error: unknown): void {
    const api = isApiError(error) ? error : null;
    const view = this.view();
    switch (api?.kind) {
      case 'unauthorized':
        handleUnauthorized(this.router, this.sessionExpired);
        break;
      case 'forbidden':
        this.forbidden.set(true);
        break;
      case 'not-found':
        // BR-20: a foreign, inactive or out-of-scope branch resets to All branches.
        if (view.branch !== null) {
          this.navigate({ ...view, branch: null }, true);
        }
        break;
      case 'validation':
      case 'bad-request': {
        // A server `400` resets the offending parameter to its default.
        const bad = FIELD_PARAMS.filter(
          (key) =>
            (api.fieldErrors[key] !== undefined ||
              api.fieldErrors[`errors.${key}`] !== undefined) &&
            this.query().get(key) !== null,
        );
        if (bad.length > 0) {
          const reset: Record<string, string | null> = { ...notificationsViewParams(view) };
          for (const key of bad) {
            reset[key] = null;
          }
          void this.router.navigate([], {
            relativeTo: this.route,
            queryParams: reset,
            queryParamsHandling: 'merge',
            replaceUrl: true,
          });
        }
        break;
      }
    }
  }

  /** BR-13: a page past the last moves to the last page. */
  private clampPage(page: NotificationsPage, request: NotificationsRequest): void {
    const last = Math.ceil(page.total / request.pageSize);
    if (page.items.length === 0 && page.total > 0 && request.page > last) {
      this.navigate({ ...this.view(), page: last }, true);
    }
  }

  private navigate(view: NotificationsView, replaceUrl = false): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: notificationsViewParams(view),
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }

  /** Any change resets to page 1 (BR-20); `page` itself is set explicitly by the pager. */
  private change(patch: Partial<NotificationsView>): void {
    this.navigate({ ...this.view(), ...patch, page: patch.page ?? 1 });
  }

  // Tabs

  selectTab(tab: NotificationsTab): void {
    if (tab !== this.view().tab) {
      this.change({ tab });
    }
  }

  onTabKeydown(event: KeyboardEvent, index: number): void {
    const last = this.tabs.length - 1;
    const keys: Record<string, number> = {
      ArrowRight: (index + 1) % this.tabs.length,
      ArrowLeft: (index + last) % this.tabs.length,
      Home: 0,
      End: last,
    };
    const next = keys[event.key];
    if (next === undefined) {
      return;
    }
    event.preventDefault();
    this.selectTab(this.tabs[next].code);
    queueMicrotask(() =>
      (event.currentTarget as HTMLElement | null)?.parentElement
        ?.querySelectorAll<HTMLElement>('[role="tab"]')
        .item(next)
        ?.focus(),
    );
  }

  // Filters

  searchChanged(text: string): void {
    this.searchText.set(text);
    this.searches$.next(text);
  }

  changeType(code: string): void {
    this.change({ type: NOTIFICATION_TYPES.find((type) => type.code === code)?.code ?? null });
  }

  changeBranch(code: string): void {
    this.change({ branch: code === ALL ? null : code });
  }

  changeDate(code: string): void {
    this.change({ date: NOTIFICATION_DATES.find((date) => date.code === code)?.code ?? 'any' });
  }

  clearFilters(): void {
    this.change({ q: '', type: null, branch: null, date: 'any' });
  }

  /** Empty state action: also leaves the Unread tab so the inbox is shown unfiltered. */
  resetView(): void {
    this.change({ q: '', type: null, branch: null, date: 'any', tab: 'all' });
  }

  // Pagination

  changePage(event: PaginatorState): void {
    const page = (event.page ?? 0) + 1;
    if (page !== this.view().page) {
      this.change({ page });
    }
  }

  changePageSize(size: number): void {
    this.change({ pageSize: size });
  }

  // Retry (only the failed part)

  retryOptions(): void {
    this.options$.next();
  }

  retryList(): void {
    const request = this.listRequest();
    if (request !== null) {
      this.list$.next(request);
    }
  }

  // Read state (BR-18, BR-19)

  /** BR-18: marks an unread row read without waiting, then opens its target. */
  select(row: RowView): void {
    if (!row.item.isRead) {
      this.markRead(row.item);
    }
    if (row.route === null) {
      this.messageService.add({ severity: 'info', summary: NOTIFICATION_TARGET_GONE_MESSAGE() });
      return;
    }
    void this.router.navigate(row.route.commands, {
      ...(row.route.queryParams === null ? {} : { queryParams: row.route.queryParams }),
    });
  }

  /** The entity link navigates by itself; an unread row is only marked read. */
  openEntity(row: RowView): void {
    if (!row.item.isRead) {
      this.markRead(row.item);
    }
  }

  openRowMenu(event: Event, row: RowView, menu: Menu): void {
    const read = row.item.isRead;
    this.rowMenu.set([
      {
        label: read
          ? t('notifications.notifications.markAsUnread')
          : t('notifications.notifications.markAsRead'),
        command: () => (read ? this.markUnread(row.item) : this.markRead(row.item)),
      },
    ]);
    menu.toggle(event);
  }

  private markRead(item: NotificationItem): void {
    this.toggleRead(item, true, this.api.read(item.id));
  }

  private markUnread(item: NotificationItem): void {
    this.toggleRead(item, false, this.api.unreadOne(item.id));
  }

  /**
   * Optimistic toggle. The subscription is not tied to the component: navigating away from a row
   * must not cancel the read call (BR-18).
   */
  private toggleRead(
    item: NotificationItem,
    isRead: boolean,
    request: Observable<{ summary: NotificationsSummary }>,
  ): void {
    const previousList = this.list();
    const previousSummary = this.summary();
    const delta = isRead ? -1 : 1;
    this.list.update((region) =>
      region.data === null
        ? region
        : {
            ...region,
            data: {
              ...region.data,
              items: region.data.items.map((row) =>
                row.id === item.id ? { ...row, isRead } : row,
              ),
              counts: {
                ...region.data.counts,
                unread: Math.max(0, region.data.counts.unread + delta),
              },
            },
          },
    );
    this.summary.update((region) =>
      region.data === null
        ? region
        : { ...region, data: { ...region.data, unread: Math.max(0, region.data.unread + delta) } },
    );
    request.subscribe({
      next: ({ summary }) => {
        this.knownUnread = summary.unread;
        this.summary.set({ status: 'ready', data: summary });
      },
      error: (error: unknown) => {
        const api = isApiError(error) ? error : null;
        if (api?.kind === 'unauthorized') {
          handleUnauthorized(this.router, this.sessionExpired);
        } else if (api?.kind === 'not-found') {
          this.messageService.add({
            severity: 'error',
            summary: NOTIFICATION_UNAVAILABLE_MESSAGE(),
          });
          this.reload();
        } else {
          this.list.set(previousList);
          this.summary.set(previousSummary);
          this.messageService.add({
            severity: 'error',
            summary: NOTIFICATION_UPDATE_FAILED_MESSAGE(),
          });
        }
      },
    });
  }

  private reload(): void {
    this.retryList();
    this.summary$.next();
  }

  /** BR-19: marks every visible unread notification, then reloads the list. */
  markAllRead(): void {
    if (this.markAllDisabled()) {
      return;
    }
    this.markingAll.set(true);
    this.api
      .readAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ summary }) => {
          this.markingAll.set(false);
          this.knownUnread = summary.unread;
          this.summary.set({ status: 'ready', data: summary });
          this.retryList();
        },
        error: (error: unknown) => {
          this.markingAll.set(false);
          if (isApiError(error) && error.kind === 'unauthorized') {
            handleUnauthorized(this.router, this.sessionExpired);
            return;
          }
          this.messageService.add({
            severity: 'error',
            summary: NOTIFICATION_UPDATE_FAILED_MESSAGE(),
          });
        },
      });
  }
}
