import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import { Subject, catchError, map, of, switchMap } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { formatDate } from '../../../customers/utils/customer-format';
import { formatMoney } from '../../../customers/utils/customer-detail-format';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { TOAST_STATE_KEY, ToastHandoff } from '../../../requests/models/requests.model';
import {
  WO_FORBIDDEN_MESSAGE,
  WO_LOAD_ERROR_MESSAGE,
  WO_NOT_AVAILABLE_MESSAGE,
  WORK_ORDER_MANAGE_ROLES,
  WorkOrderDetail,
} from '../../models/work-order.model';
import { WorkOrdersService } from '../../services/work-orders.service';
import {
  FREQUENCY_LABELS,
  JOB_TYPE_LABELS,
  PRIORITY_LABELS,
  SOURCE_LABELS,
  STATUS_LABELS,
  visitStatusLabel,
  WINDOW_LABELS,
  durationLabel,
  plainDate,
} from '../../utils/work-order-format';

type PageState = 'loading' | 'ready' | 'forbidden' | 'not-available' | 'error';
type LoadResult =
  | { readonly kind: 'ready'; readonly detail: WorkOrderDetail }
  | { readonly kind: 'state'; readonly state: PageState }
  | { readonly kind: 'unauthorized' };

const NO_VISITS_MESSAGE = 'No visits yet.';

/** Read-only job detail (`/jobs/:id`, BR-22). Read roles only; Manage roles may continue a draft. */
@Component({
  selector: 'app-job-detail',
  imports: [RouterLink, ButtonDirective, Message, Skeleton, Toast],
  providers: [MessageService],
  templateUrl: './job-detail.html',
  styleUrl: './job-detail.scss',
})
export class JobDetail {
  private readonly workOrders = inject(WorkOrdersService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  readonly id = this.route.snapshot.paramMap.get('id') ?? '';
  readonly forbiddenMessage = WO_FORBIDDEN_MESSAGE;
  readonly notAvailableMessage = WO_NOT_AVAILABLE_MESSAGE;
  readonly errorMessage = WO_LOAD_ERROR_MESSAGE;
  readonly noVisits = NO_VISITS_MESSAGE;
  readonly priorityLabels = PRIORITY_LABELS;
  readonly statusLabels = STATUS_LABELS;
  readonly visitLabel = visitStatusLabel;
  readonly jobTypeLabels = JOB_TYPE_LABELS;
  readonly sourceLabels = SOURCE_LABELS;
  readonly windowLabels = WINDOW_LABELS;
  readonly frequencyLabels = FREQUENCY_LABELS;
  readonly durationLabel = durationLabel;

  /** UX only: Read = Manage + viewer (BR-01); the backend decides. */
  private readonly canRead = computed(() =>
    [...WORK_ORDER_MANAGE_ROLES, 'viewer'].includes(this.sessionService.session()?.role.code ?? ''),
  );
  readonly sessionExpired = signal(false);

  readonly state = signal<PageState>('loading');
  readonly detail = signal<WorkOrderDetail | null>(null);
  readonly approvedTotal = computed(() => {
    const quote = this.detail()?.quote;
    return quote === undefined ? '' : formatMoney(quote.approvedTotal, quote.currency);
  });
  readonly skillNames = computed(() => (this.detail()?.skills ?? []).map((s) => s.name).join(', '));
  readonly preferred = computed(() => {
    const detail = this.detail();
    if (detail === null || detail.preferredDate === null) {
      return 'No preferred date';
    }
    const zone = detail.branch.timezone;
    // The instant is the branch-local start of the window; the date is shown in the branch zone.
    const date =
      detail.preferredStart !== null && zone !== null
        ? formatDate(detail.preferredStart, zone)
        : plainDate(detail.preferredDate);
    return `${date} · ${WINDOW_LABELS[detail.arrivalWindow]}`;
  });

  private readonly loads = new Subject<void>();

  constructor() {
    // A toast handed over by the editor (created, already created); shown once the outlet exists.
    const handoff = this.router.currentNavigation()?.extras.state?.[TOAST_STATE_KEY] as
      ToastHandoff | undefined;
    if (handoff !== undefined) {
      afterNextRender(() => this.messageService.add(handoff), { injector: this.injector });
    }

    this.loads
      .pipe(
        switchMap(() => {
          this.state.set('loading');
          return this.workOrders.detail(this.id).pipe(
            map((detail): LoadResult => ({ kind: 'ready', detail })),
            catchError((error: unknown) => {
              switch (isApiError(error) ? error.kind : null) {
                case 'unauthorized':
                  return of<LoadResult>({ kind: 'unauthorized' });
                case 'forbidden':
                  return of<LoadResult>({ kind: 'state', state: 'forbidden' });
                case 'not-found':
                  return of<LoadResult>({ kind: 'state', state: 'not-available' });
                default:
                  return of<LoadResult>({ kind: 'state', state: 'error' });
              }
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        if (result.kind === 'unauthorized') {
          handleUnauthorized(this.router, this.sessionExpired);
        } else if (result.kind === 'state') {
          this.settle(result.state);
        } else {
          this.detail.set(result.detail);
          this.settle('ready');
        }
      });

    if (this.canRead()) {
      this.loads.next();
    } else {
      this.settle('forbidden');
    }
  }

  private settle(state: PageState): void {
    this.state.set(state);
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }

  retry(): void {
    this.loads.next();
  }
}
