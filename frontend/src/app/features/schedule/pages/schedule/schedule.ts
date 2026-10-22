import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import { Observable } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { MD_QUERY, watchMedia } from '../../../../core/config/breakpoints';
import { SessionService } from '../../../../core/services/session.service';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { DispatchDrawer, DispatchSaved } from '../../components/dispatch-drawer/dispatch-drawer';
import { ScheduleCalendar } from '../../components/schedule-calendar/schedule-calendar';
import { ScheduleToolbar } from '../../components/schedule-toolbar/schedule-toolbar';
import { UnscheduledPanel } from '../../components/unscheduled-panel/unscheduled-panel';
import { ScheduleView } from '../../models/schedule.model';
import { ProposedBlock, SchedulePageStore } from '../../services/schedule-page.store';
import {
  isValidDate,
  nowMinutes,
  rangeDays,
  rangeLabel,
  shiftDate,
  todayInZone,
} from '../../utils/schedule-range';

import { t } from '../../../../core/i18n/translate';
export const FORBIDDEN_MESSAGE = (): string => t('schedule.schedule.forbiddenMessage');
export const NO_BRANCHES_MESSAGE = (): string => t('schedule.schedule.noBranchesMessage');
export const LOAD_ERROR_MESSAGE = (): string => t('schedule.schedule.loadErrorMessage');
export const VISIT_UNAVAILABLE_MESSAGE = (): string =>
  t('schedule.schedule.visitUnavailableMessage');

/** BR-01: Manage = owner, operations manager, dispatcher; Read adds viewer. */
const READ_ROLES: readonly string[] = ['owner', 'operations_manager', 'dispatcher', 'viewer'];
/** BR-21: three-area layout from 1280px. */
const DESKTOP_QUERY = '(min-width: 80rem)';
const CLOCK_TICK_MS = 60_000;

type Layout = 'mobile' | 'tablet' | 'desktop';

/**
 * Dispatch calendar (`/schedule`): URL-driven branch/view/date, unscheduled panel, calendar and the
 * Assign work order drawer. Roles without Read get the forbidden state with no data requests
 * (BR-01); backend authorization is authoritative.
 */
const KEEP_SELECTION_SELECTOR =
  '.up__card, .cal__block, app-dispatch-drawer, app-drawer-shell, button, a, input, textarea, select, label, [role="dialog"], [class*="p-overlay"], [class*="-overlay"], .p-toast, .p-dialog, .p-confirmdialog';

@Component({
  selector: 'app-schedule',
  host: { '(document:click)': 'onBackgroundClick($event)' },
  imports: [
    TranslocoPipe,
    ButtonDirective,
    DiscardChangesDialog,
    DispatchDrawer,
    Message,
    ScheduleCalendar,
    ScheduleToolbar,
    Skeleton,
    Toast,
    UnscheduledPanel,
  ],
  providers: [MessageService, ConfirmationService, SchedulePageStore],
  templateUrl: './schedule.html',
  styleUrl: './schedule.scss',
})
export class SchedulePage {
  protected readonly store = inject(SchedulePageStore);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);

  get forbiddenMessage(): string {
    return FORBIDDEN_MESSAGE();
  }
  get noBranchesMessage(): string {
    return NO_BRANCHES_MESSAGE();
  }
  get loadErrorMessage(): string {
    return LOAD_ERROR_MESSAGE();
  }
  readonly skeletons = [0, 1, 2];

  private readonly roleCode = computed(() => this.sessionService.session()?.role.code ?? '');
  private readonly canRead = computed(() => READ_ROLES.includes(this.roleCode()));
  readonly forbidden = computed(() => !this.canRead() || this.store.access() === 'forbidden');
  private readonly sessionExpired = signal(false);

  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  readonly layout = signal<Layout>('desktop');
  readonly compact = computed(() => this.layout() === 'mobile');
  readonly panelOpen = signal(false);
  private readonly now = signal(new Date());

  readonly view = computed<ScheduleView>(() => this.store.route()?.view ?? 'week');
  readonly date = computed(() => this.store.route()?.date ?? '');
  readonly label = computed(() => (this.date() === '' ? '' : rangeLabel(this.view(), this.date())));
  readonly today = computed(() => todayInZone(this.store.timezone(), this.now()));
  readonly clockMinutes = computed(() => nowMinutes(this.store.timezone(), this.now()));
  readonly branches = computed(() => this.store.options()?.branches ?? []);
  readonly skills = computed(() => this.store.options()?.skills ?? []);

  private lastProposedDate: string | null = null;
  private lastVisitParam: string | null = null;

  constructor() {
    const desktop = watchMedia(DESKTOP_QUERY, () => this.updateLayout(), true);
    const md = watchMedia(MD_QUERY, () => this.updateLayout(), true);
    this.destroyRef.onDestroy(() => {
      desktop.stop();
      md.stop();
    });
    this.updateLayout(desktop.matches, md.matches);

    const timer = setInterval(() => this.now.set(new Date()), CLOCK_TICK_MS);
    this.destroyRef.onDestroy(() => clearInterval(timer));

    if (!this.forbidden()) {
      this.store.loadOptions();
    }

    // URL query (branch, view, date) is the single source of the route state.
    effect(() => {
      const params = this.queryParams();
      if (this.forbidden() || this.store.optionsStatus() !== 'ready') {
        return;
      }
      untracked(() => {
        const resolved = this.syncRoute(
          params.get('branch'),
          params.get('view'),
          params.get('date'),
        );
        if (resolved) {
          this.syncVisit(params.get('visit'));
        }
      });
    });

    effect(() => {
      if (this.store.access() === 'unauthorized') {
        untracked(() => handleUnauthorized(this.router, this.sessionExpired));
      }
    });

    // `404` branch falls back to the default branch (UI states).
    effect(() => {
      const failed =
        this.store.calendarError()?.kind === 'not-found' ||
        this.store.panelError()?.kind === 'not-found';
      if (failed) {
        untracked(() => this.fallbackBranch());
      }
    });

    // The calendar moves to the range containing a newly proposed date (replaceUrl, no prompt).
    effect(() => {
      const date = this.store.proposed()?.date ?? null;
      untracked(() => {
        if (date === this.lastProposedDate) {
          return;
        }
        this.lastProposedDate = date;
        const route = this.store.route();
        if (
          route !== null &&
          isValidDate(date) &&
          !rangeDays(route.view, route.date).includes(date)
        ) {
          this.go({ date }, true);
        }
      });
    });
  }

  private updateLayout(desktop?: boolean, md?: boolean): void {
    const isDesktop = desktop ?? window.matchMedia(DESKTOP_QUERY).matches;
    const isMd = md ?? window.matchMedia(MD_QUERY).matches;
    this.layout.set(isDesktop ? 'desktop' : isMd ? 'tablet' : 'mobile');
  }

  /** Applies the route state; false when the URL had to be rewritten first or no branch exists. */
  private syncRoute(
    requestedBranch: string | null,
    viewParam: string | null,
    dateParam: string | null,
  ): boolean {
    const branchId = this.store.resolveBranchId(requestedBranch);
    if (branchId === null) {
      return false;
    }
    const timezone = this.branches().find((branch) => branch.id === branchId)?.timezone ?? 'UTC';
    const view: ScheduleView =
      viewParam === 'day' || viewParam === 'week' ? viewParam : this.compact() ? 'day' : 'week';
    const date = isValidDate(dateParam) ? dateParam : todayInZone(timezone);
    if (requestedBranch !== branchId || viewParam !== view || dateParam !== date) {
      this.go({ branch: branchId, view, date }, true);
      return false;
    }
    this.store.applyRoute({ branchId, view, date });
    return true;
  }

  /**
   * operations-dashboard BR-18: a new `visit` parameter opens the dispatch drawer once the route
   * state is resolved. Later URL changes with the same value never reopen a closed drawer.
   */
  private syncVisit(visitParam: string | null): void {
    if (visitParam === this.lastVisitParam) {
      return;
    }
    this.lastVisitParam = visitParam;
    if (visitParam !== null) {
      this.openVisit(visitParam);
    }
  }

  private fallbackBranch(): void {
    const fallback = this.store.defaultBranchId();
    const current = this.store.route()?.branchId;
    if (fallback !== null && fallback !== current) {
      this.go({ branch: fallback }, true);
    }
  }

  private go(params: Record<string, string | null>, replaceUrl = false): void {
    // A `visit` left in the URL by a closed drawer is dropped with the next navigation.
    const stale = this.queryParams().get('visit') !== null && this.store.selectedVisitId() === null;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: stale ? { visit: null, ...params } : params,
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }

  // Navigation and filters

  onToday(): void {
    this.whenClean(() => this.go({ date: this.today() }));
  }

  onShift(direction: -1 | 1): void {
    this.whenClean(() => this.go({ date: shiftDate(this.view(), this.date(), direction) }));
  }

  onView(view: ScheduleView): void {
    if (view !== this.view()) {
      this.whenClean(() => this.go({ view }));
    }
  }

  onBranch(branchId: string, toolbar: ScheduleToolbar): void {
    if (branchId === this.store.route()?.branchId) {
      return;
    }
    this.whenClean(
      () => {
        this.closeDrawer();
        this.go({ branch: branchId });
      },
      () => toolbar.resetBranch(),
    );
  }

  // Drawer

  openVisit(visitId: string): void {
    if (visitId === this.store.selectedVisitId()) {
      return;
    }
    this.whenClean(() => this.store.selectedVisitId.set(visitId));
  }

  private readonly drawer = viewChild(DispatchDrawer);

  /** A click on empty page background deselects the visit; interactive targets are ignored. */
  onBackgroundClick(event: MouseEvent): void {
    if (this.store.selectedVisitId() === null) {
      return;
    }
    const target = event.target;
    if (target instanceof Element && target.closest(KEEP_SELECTION_SELECTOR) === null) {
      this.drawer()?.requestClose();
    }
  }

  onDrawerCloseRequested(): void {
    this.whenClean(() => this.closeDrawer());
  }

  onDrawerSaved(saved: DispatchSaved): void {
    this.messageService.add({ severity: 'success', summary: saved.message });
    this.closeDrawer();
    this.store.refresh();
  }

  /** BR-21: a cancelled visit closes the drawer and refreshes the calendar and Unscheduled panel. */
  onDrawerCancelled(message: string): void {
    this.messageService.add({ severity: 'success', summary: message });
    this.closeDrawer();
    this.store.refresh();
  }

  onDrawerUnavailable(): void {
    this.messageService.add({ severity: 'error', summary: VISIT_UNAVAILABLE_MESSAGE() });
    this.closeDrawer();
    this.store.refresh();
  }

  onProposed(block: ProposedBlock | null): void {
    this.store.proposed.set(block);
  }

  private closeDrawer(): void {
    this.store.selectedVisitId.set(null);
    this.store.proposed.set(null);
    this.store.drawerDirty.set(false);
    // BR-18: closing the drawer removes `visit` from the URL.
    if (this.queryParams().get('visit') !== null) {
      this.go({}, true);
    }
  }

  // Unsaved-changes handling (shared discard dialog)

  private whenClean(action: () => void, onKeep?: () => void): void {
    if (!this.store.drawerDirty()) {
      action();
      return;
    }
    this.confirmDiscard().subscribe((leave) => {
      if (leave) {
        this.closeDrawer();
        action();
      } else {
        onKeep?.();
      }
    });
  }

  canLeave(): boolean | Observable<boolean> {
    return this.sessionExpired() || !this.store.drawerDirty() ? true : this.confirmDiscard();
  }

  private confirmDiscard(): Observable<boolean> {
    return new Observable<boolean>((subscriber) => {
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: t('schedule.schedule.thisVisit'),
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
}
