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

export const FORBIDDEN_MESSAGE = "You don't have access to Schedule.";
export const NO_BRANCHES_MESSAGE = 'No branches available.';
export const LOAD_ERROR_MESSAGE = "We couldn't load the schedule. Try again.";
export const VISIT_UNAVAILABLE_MESSAGE = "This visit isn't available.";

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

  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly noBranchesMessage = NO_BRANCHES_MESSAGE;
  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
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
      untracked(() => this.syncRoute(params.get('branch'), params.get('view'), params.get('date')));
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

  private syncRoute(
    requestedBranch: string | null,
    viewParam: string | null,
    dateParam: string | null,
  ): void {
    const branchId = this.store.resolveBranchId(requestedBranch);
    if (branchId === null) {
      return;
    }
    const timezone = this.branches().find((branch) => branch.id === branchId)?.timezone ?? 'UTC';
    const view: ScheduleView =
      viewParam === 'day' || viewParam === 'week' ? viewParam : this.compact() ? 'day' : 'week';
    const date = isValidDate(dateParam) ? dateParam : todayInZone(timezone);
    if (requestedBranch !== branchId || viewParam !== view || dateParam !== date) {
      this.go({ branch: branchId, view, date }, true);
      return;
    }
    this.store.applyRoute({ branchId, view, date });
  }

  private fallbackBranch(): void {
    const fallback = this.store.defaultBranchId();
    const current = this.store.route()?.branchId;
    if (fallback !== null && fallback !== current) {
      this.go({ branch: fallback }, true);
    }
  }

  private go(params: Record<string, string>, replaceUrl = false): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: params,
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

  onDrawerUnavailable(): void {
    this.messageService.add({ severity: 'error', summary: VISIT_UNAVAILABLE_MESSAGE });
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
          subject: 'this visit',
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
