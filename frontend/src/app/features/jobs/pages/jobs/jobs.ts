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
import { Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Subject, catchError, map, of, switchMap } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { formatDate } from '../../../customers/utils/customer-format';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { localZone } from '../../../quotes/utils/quote-format';
import {
  JOBS_EMPTY_MESSAGE,
  JOBS_LOAD_ERROR_MESSAGE,
  WO_FORBIDDEN_MESSAGE,
  WORK_ORDER_MANAGE_ROLES,
  WorkOrderList,
  WorkOrderListItem,
} from '../../models/work-order.model';
import { JOBS_PAGE_SIZE, WorkOrdersService } from '../../services/work-orders.service';
import { PRIORITY_LABELS, STATUS_LABELS } from '../../utils/work-order-format';

type PageState = 'loading' | 'ready' | 'forbidden' | 'error';
type LoadResult =
  | { readonly kind: 'ready'; readonly list: WorkOrderList }
  | { readonly kind: 'state'; readonly state: PageState }
  | { readonly kind: 'unauthorized' };

/** Jobs list (`/jobs`, BR-21): visible work orders, newest first, 25 per page. Read roles only. */
@Component({
  selector: 'app-jobs',
  imports: [RouterLink, ButtonDirective, Message, Skeleton],
  templateUrl: './jobs.html',
  styleUrl: './jobs.scss',
})
export class Jobs {
  private readonly workOrders = inject(WorkOrdersService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  readonly forbiddenMessage = WO_FORBIDDEN_MESSAGE;
  readonly errorMessage = JOBS_LOAD_ERROR_MESSAGE;
  readonly emptyMessage = JOBS_EMPTY_MESSAGE;
  readonly priorityLabels = PRIORITY_LABELS;
  readonly statusLabels = STATUS_LABELS;

  /** UX only: Read = Manage + viewer (BR-01); the backend decides. */
  private readonly canRead = computed(() =>
    [...WORK_ORDER_MANAGE_ROLES, 'viewer'].includes(this.sessionService.session()?.role.code ?? ''),
  );
  readonly sessionExpired = signal(false);

  readonly state = signal<PageState>('loading');
  readonly page = signal(1);
  readonly items = signal<readonly WorkOrderListItem[]>([]);
  readonly total = signal(0);
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / JOBS_PAGE_SIZE)));

  private readonly loads = new Subject<void>();

  constructor() {
    this.loads
      .pipe(
        switchMap(() => {
          this.state.set('loading');
          return this.workOrders.list(this.page()).pipe(
            map((list): LoadResult => ({ kind: 'ready', list })),
            catchError((error: unknown) => {
              switch (isApiError(error) ? error.kind : null) {
                case 'unauthorized':
                  return of<LoadResult>({ kind: 'unauthorized' });
                case 'forbidden':
                  return of<LoadResult>({ kind: 'state', state: 'forbidden' });
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
          this.items.set(result.list.items);
          this.total.set(result.list.total);
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

  go(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.pageCount()));
    this.loads.next();
  }

  created(item: WorkOrderListItem): string {
    return formatDate(item.createdAt, localZone());
  }

  open(item: WorkOrderListItem): void {
    void this.router.navigate(['/jobs', item.id]);
  }
}
