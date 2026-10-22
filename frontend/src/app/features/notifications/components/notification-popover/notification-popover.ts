import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
  viewChildren,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { MessageService } from 'primeng/api';
import { Popover } from 'primeng/popover';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import { EMPTY, Subject, catchError, filter, switchMap, tap } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { isApiError } from '../../../../core/models/api-error.model';
import { NotificationUnreadService } from '../../../../core/services/notification-unread.service';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import {
  NOTIFICATIONS_LOAD_ERROR_MESSAGE,
  NOTIFICATION_TARGET_GONE_MESSAGE,
  NOTIFICATION_UNAVAILABLE_MESSAGE,
  NOTIFICATION_UPDATE_FAILED_MESSAGE,
  NotificationItem,
  NotificationsPage,
  NotificationsRequest,
} from '../../models/notifications.model';
import { NotificationsService } from '../../services/notifications.service';
import { fullDateTime, iconFor, relativeTime, targetRoute } from '../../utils/notifications-format';

import { t } from '../../../../core/i18n/translate';
import { NotificationTextPipe } from '../../utils/notification-text.pipe';
/** BR-26: the five latest notifications visible to the caller. */
const POPOVER_REQUEST: NotificationsRequest = {
  tab: 'all',
  q: '',
  type: null,
  branchId: null,
  date: 'any',
  page: 1,
  pageSize: 5,
};

const NARROW_VIEWPORT = 480;
const ARROW_LEFT_VAR = '--p-popover-arrow-left';

/** Panel size through the popover's own `style` input (the panel is appended to `body`). */
const PANEL_STYLE = {
  'inline-size': 'min(26rem, calc(100vw - 1rem))',
  '--p-popover-content-padding': '0',
};

interface PopoverRow {
  readonly item: NotificationItem;
  readonly icon: string;
  readonly tone: string;
  readonly relative: string;
  readonly full: string;
  readonly name: string;
  readonly route: { commands: string[]; queryParams: Record<string, string> | null } | null;
}

/** Notification popover of the shell bell (BR-26, Design 33). Adds no polling and no summary request. */
@Component({
  selector: 'app-notification-popover',
  imports: [NotificationTextPipe, TranslocoPipe, Popover, Skeleton, Toast],
  providers: [MessageService],
  templateUrl: './notification-popover.html',
  styleUrl: './notification-popover.scss',
})
export class NotificationPopover {
  private readonly api = inject(NotificationsService);
  private readonly unread = inject(NotificationUnreadService);
  private readonly messageService = inject(MessageService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly popover = viewChild.required(Popover);
  private readonly rowEls = viewChildren<ElementRef<HTMLElement>>('rowEl');
  private readonly markAllEl = viewChild<ElementRef<HTMLButtonElement>>('markAll');
  private readonly viewAllEl = viewChild<ElementRef<HTMLButtonElement>>('footerLink');

  /** Element that receives focus when the popover closes (the bell). */
  readonly trigger = input.required<HTMLElement>();
  readonly closed = output<void>();

  readonly panelStyle = PANEL_STYLE;
  get errorMessage(): string {
    return NOTIFICATIONS_LOAD_ERROR_MESSAGE();
  }
  readonly skeletonRows = [1, 2, 3, 4, 5];
  readonly open = signal(false);
  readonly status = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  private readonly page = signal<NotificationsPage | null>(null);
  private readonly now = signal(new Date());
  private readonly markingAll = signal(false);
  private readonly sessionExpired = signal(false);
  private readonly load$ = new Subject<void>();
  private shown = false;
  private pendingFocus = false;
  private skipFocusReturn = false;

  readonly markAllDisabled = computed(() => this.markingAll() || (this.unread.count() ?? 1) === 0);
  readonly rows = computed<PopoverRow[]>(() => {
    const data = this.page();
    const zone = data?.timeZone ?? 'UTC';
    const now = this.now();
    return (data?.items ?? []).map((item) => {
      const { icon, tone } = iconFor(item);
      const relative = relativeTime(item.createdAt, zone, now);
      return {
        item,
        icon,
        tone,
        relative,
        full: fullDateTime(item.createdAt, zone),
        name: `${item.title}, ${item.isRead ? t('notifications.notificationPopover.read') : t('notifications.notificationPopover.unread')}, ${relative}`,
        route: item.target === null ? null : targetRoute(item.target.kind, item.target.id),
      };
    });
  });

  constructor() {
    this.load$
      .pipe(
        switchMap(() => {
          this.status.set('loading');
          return this.api.list(POPOVER_REQUEST).pipe(
            tap((page) => {
              this.page.set(page);
              this.now.set(new Date());
              this.status.set('ready');
              this.settle();
            }),
            catchError((error: unknown) => {
              this.page.set(null);
              this.status.set('error');
              this.handleFailure(error);
              this.settle();
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.hide());
  }

  /** Opens (and reloads the five rows) or closes the popover anchored to the event target. */
  toggle(event: Event): void {
    if (this.open()) {
      this.hide();
      return;
    }
    this.open.set(true);
    this.shown = false;
    this.pendingFocus = true;
    this.skipFocusReturn = false;
    this.load$.next();
    this.popover().show(event);
  }

  hide(): void {
    if (this.open()) {
      this.popover().hide();
    }
  }

  reload(): void {
    this.load$.next();
  }

  onShow(): void {
    this.shown = true;
    this.centerOnNarrowScreens();
    this.focusInitial();
  }

  /** Below 480 px the panel is centered in the viewport and its arrow keeps pointing at the bell. */
  private centerOnNarrowScreens(): void {
    if (window.innerWidth >= NARROW_VIEWPORT) {
      return;
    }
    // PrimeNG positions the panel after `onShow`, so center it on the next frame.
    requestAnimationFrame(() => {
      const panel = document.querySelector<HTMLElement>('.notif-pop');
      if (!panel) {
        return;
      }
      // `offsetLeft` ignores the enter animation's scale transform, unlike the bounding rect.
      const target = (document.documentElement.clientWidth - panel.offsetWidth) / 2;
      const shift = panel.offsetLeft - target;
      const arrow = parseFloat(panel.style.getPropertyValue(ARROW_LEFT_VAR));
      panel.style.left = `${target}px`;
      if (!Number.isNaN(arrow)) {
        panel.style.setProperty(ARROW_LEFT_VAR, `${arrow + shift}px`);
      }
    });
  }

  onHide(): void {
    const skip = this.skipFocusReturn;
    this.open.set(false);
    this.shown = false;
    this.pendingFocus = false;
    this.skipFocusReturn = false;
    if (!skip) {
      this.trigger().focus();
    }
    this.closed.emit();
  }

  /** BR-26/BR-18: an unread row is marked read and shown read at once; then the popover closes. */
  select(row: PopoverRow): void {
    if (!row.item.isRead) {
      this.markRead(row.item);
    }
    if (row.route === null) {
      this.messageService.add({ severity: 'info', summary: NOTIFICATION_TARGET_GONE_MESSAGE() });
      this.hide();
      return;
    }
    this.skipFocusReturn = true;
    this.hide();
    void this.router.navigate(row.route.commands, {
      ...(row.route.queryParams === null ? {} : { queryParams: row.route.queryParams }),
    });
  }

  viewAll(): void {
    this.skipFocusReturn = true;
    this.hide();
    void this.router.navigate(['/notifications']);
  }

  markAllRead(): void {
    if (this.markAllDisabled()) {
      return;
    }
    this.markingAll.set(true);
    this.api
      .readAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.markingAll.set(false);
          this.reload();
        },
        error: (error: unknown) => {
          this.markingAll.set(false);
          this.handleFailure(error, true);
        },
      });
  }

  private markRead(item: NotificationItem): void {
    const before = this.page();
    this.setRead(item.id);
    this.api.read(item.id).subscribe({
      error: (error: unknown) => {
        if (isApiError(error) && error.kind === 'not-found') {
          this.messageService.add({
            severity: 'error',
            summary: NOTIFICATION_UNAVAILABLE_MESSAGE(),
          });
          this.reload();
        } else {
          this.page.set(before);
          this.handleFailure(error, true);
        }
      },
    });
  }

  private setRead(id: string): void {
    this.page.update((data) =>
      data === null
        ? data
        : {
            ...data,
            items: data.items.map((it) => (it.id === id ? { ...it, isRead: true } : it)),
          },
    );
  }

  private handleFailure(error: unknown, notify = false): void {
    const kind = isApiError(error) ? error.kind : null;
    if (kind === 'unauthorized') {
      handleUnauthorized(this.router, this.sessionExpired);
    } else if (kind === 'forbidden') {
      this.hide();
    } else if (notify) {
      this.messageService.add({ severity: 'error', summary: NOTIFICATION_UPDATE_FAILED_MESSAGE() });
    }
  }

  private settle(): void {
    if (this.shown && this.pendingFocus) {
      this.focusInitial();
    }
  }

  /** First row, else the header action, else the footer link (a disabled action cannot take focus). */
  private focusInitial(): void {
    if (this.status() !== 'loading') {
      this.pendingFocus = false;
    }
    afterNextRender(
      () => {
        const target =
          this.rowEls()[0]?.nativeElement ??
          (this.markAllDisabled() ? null : this.markAllEl()?.nativeElement) ??
          this.viewAllEl()?.nativeElement;
        target?.focus();
      },
      { injector: this.injector },
    );
  }
}
