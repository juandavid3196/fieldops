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
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import { Observable, Subject, catchError, map, of, switchMap } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { formatMoney } from '../../../customers/utils/customer-detail-format';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import {
  MANAGER_ROLES,
  READ_ROLES,
  TOAST_STATE_KEY,
  ToastHandoff,
} from '../../../requests/models/requests.model';
import { QuoteCustomerView } from '../../components/quote-customer-view/quote-customer-view';
import { QuoteResendDialog } from '../../components/quote-resend-dialog/quote-resend-dialog';
import { QuoteSummary } from '../../components/quote-summary/quote-summary';
import {
  EMAIL_FAILED_MESSAGE,
  NO_RECIPIENT_MESSAGE,
  QUOTE_CHANGED_MESSAGE,
  QUOTE_ID_PATTERN,
  QUOTE_LOAD_ERROR_MESSAGE,
  QUOTE_SAVE_FAILED_MESSAGE,
  QUOTE_UNAVAILABLE_MESSAGE,
  QuoteDetail,
  QuoteResponse,
  QuoteResponseType,
  QuoteVersion,
  SendResult,
  VIEW_FORBIDDEN_MESSAGE,
} from '../../models/quote.model';
import { QuotesService } from '../../services/quotes.service';
import {
  STATUS_LABELS,
  dateOnlyLabel,
  dateTimeAt,
  defaultEmailMessage,
  localZone,
  versionView,
} from '../../utils/quote-format';
import { formatDate } from '../../../customers/utils/customer-format';

export const NOT_SENT_MESSAGE = "This quote hasn't been sent yet.";
export const NO_RESPONSE_MESSAGE = 'No response from the customer yet.';
export const RESPONSE_LABELS: Readonly<Record<QuoteResponseType, string>> = {
  approved: 'Approved',
  rejected: 'Declined',
  clarification_requested: 'Question',
};
export const CANCELLED_MESSAGE = 'This quote was cancelled.';
const REVISABLE: readonly string[] = ['sent', 'clarification_requested', 'rejected', 'expired'];

type PageState = 'loading' | 'ready' | 'forbidden' | 'not-available' | 'error';
type VersionState = 'loading' | 'ready' | 'error';

type LoadResult =
  | { readonly kind: 'ready'; readonly detail: QuoteDetail }
  | { readonly kind: 'state'; readonly state: PageState }
  | { readonly kind: 'unauthorized' };

/**
 * Read-only quote view (`/quotes/:quoteId`, BR-30): the selected frozen version in the customer
 * layout plus internal data. Owner and Dispatcher get the revise, continue and resend actions;
 * read roles see the same data with no actions. Technicians and unknown roles are forbidden with
 * no calls.
 */
@Component({
  selector: 'app-quote-view',
  imports: [
    FormsModule,
    RouterLink,
    ButtonDirective,
    Message,
    Select,
    Skeleton,
    Toast,
    QuoteCustomerView,
    QuoteResendDialog,
    QuoteSummary,
  ],
  providers: [MessageService],
  templateUrl: './quote-view.html',
  styleUrl: './quote-view.scss',
})
export class QuoteView {
  private readonly quotes = inject(QuotesService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  readonly quoteId = this.route.snapshot.paramMap.get('quoteId') ?? '';
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  readonly forbiddenMessage = VIEW_FORBIDDEN_MESSAGE;
  readonly unavailableMessage = QUOTE_UNAVAILABLE_MESSAGE;
  readonly loadErrorMessage = QUOTE_LOAD_ERROR_MESSAGE;
  readonly notSentMessage = NOT_SENT_MESSAGE;
  readonly cancelledMessage = CANCELLED_MESSAGE;
  readonly noRecipientMessage = NO_RECIPIENT_MESSAGE;
  readonly noResponseMessage = NO_RESPONSE_MESSAGE;
  readonly responseLabels = RESPONSE_LABELS;

  private readonly roleCode = computed(() => this.sessionService.session()?.role.code ?? '');
  /** UX only: the backend decides (BR-07). */
  readonly canManage = computed(() => MANAGER_ROLES.includes(this.roleCode()));
  private readonly canRead = computed(
    () => this.canManage() || READ_ROLES.includes(this.roleCode()),
  );
  readonly sessionExpired = signal(false);

  readonly state = signal<PageState>('loading');
  readonly detail = signal<QuoteDetail | null>(null);
  readonly selected = signal<number | null>(null);
  readonly version = signal<QuoteVersion | null>(null);
  readonly versionState = signal<VersionState>('loading');
  readonly busy = signal(false);
  readonly resendOpen = signal(false);
  readonly resendError = signal<string | null>(null);

  readonly statusLabel = computed(() => {
    const status = this.detail()?.displayStatus;
    return status === undefined ? '' : STATUS_LABELS[status];
  });
  readonly requestQueryParams = computed(() => ({ request: this.detail()?.request.id ?? '' }));
  readonly versionOptions = computed(() =>
    (this.detail()?.sentVersions ?? []).map((version) => ({
      code: version.versionNo,
      label: `Version ${version.versionNo} · Sent ${formatDate(version.sentAt, localZone())} · ${version.isCurrent ? 'Current' : 'Superseded'}`,
    })),
  );
  readonly view = computed(() => {
    const detail = this.detail();
    const version = this.version();
    return detail === null || version === null ? null : versionView(detail, version);
  });
  readonly margin = computed(() => {
    const margin = this.version()?.margin ?? null;
    const currency = this.version()?.currency ?? '';
    if (margin === null) {
      return null;
    }
    return margin.percent === null
      ? '—'
      : `${margin.percent}% · ${formatMoney(margin.grossProfit, currency)}`;
  });
  /** Customer responses of the selected version, newest first (BR-23). */
  readonly versionResponses = computed<readonly QuoteResponse[]>(() => {
    const selected = this.selected();
    return (this.detail()?.responses ?? []).filter((response) => response.versionNo === selected);
  });
  readonly validUntil = computed(() => {
    const value = this.version()?.validUntil;
    return value === undefined ? '' : dateOnlyLabel(value);
  });
  /** A revision in progress: Continue editing and its banner (BR-30). */
  readonly revisionNo = computed(() => {
    const detail = this.detail();
    return detail !== null && detail.sentVersions.length > 0
      ? (detail.draft?.versionNo ?? null)
      : null;
  });
  readonly canRevise = computed(() => {
    const detail = this.detail();
    return (
      this.canManage() &&
      detail !== null &&
      detail.draft === null &&
      REVISABLE.includes(detail.status)
    );
  });
  /** BR-04: the work order action of an approved quote; `null` when none applies. */
  readonly workOrderAction = computed<{
    readonly label: string;
    readonly link: readonly string[];
    readonly primary: boolean;
  } | null>(() => {
    const detail = this.detail();
    if (detail === null || detail.status !== 'approved') {
      return null;
    }
    const manage = detail.canManageWorkOrders;
    const order = detail.workOrder;
    if (order === null) {
      return manage
        ? { label: 'Create work order', link: ['/quotes', detail.id, 'work-order'], primary: true }
        : null;
    }
    if (order.status === 'draft') {
      return manage
        ? { label: 'Continue draft', link: ['/quotes', detail.id, 'work-order'], primary: false }
        : null;
    }
    return manage || this.roleCode() === 'viewer'
      ? { label: 'View job', link: ['/jobs', order.id], primary: false }
      : null;
  });
  readonly canResend = computed(() => {
    const detail = this.detail();
    return (
      this.canManage() && detail !== null && detail.status === 'sent' && !!detail.recipient.email
    );
  });
  readonly unsent = computed(() => {
    const detail = this.detail();
    return detail !== null && detail.sentVersions.length === 0;
  });
  readonly resendMessage = computed(() => {
    const detail = this.detail();
    return detail === null ? '' : defaultEmailMessage(detail.customer.name, detail.request.title);
  });

  private readonly loads = new Subject<void>();
  private readonly versionLoads = new Subject<number>();

  constructor() {
    // A toast handed over by the editor (send, discard); shown once the toast outlet exists.
    const handoff = this.router.currentNavigation()?.extras.state?.[TOAST_STATE_KEY] as
      ToastHandoff | undefined;
    if (handoff !== undefined) {
      afterNextRender(() => this.messageService.add(handoff), { injector: this.injector });
    }

    this.loads
      .pipe(
        switchMap(() => this.load$()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handleLoad(result));

    this.versionLoads
      .pipe(
        switchMap((versionNo) => {
          this.versionState.set('loading');
          return this.quotes.version(this.quoteId, versionNo).pipe(
            map((version) => ({ ok: true as const, version })),
            catchError((error: unknown) =>
              of({ ok: false as const, error: isApiError(error) ? error : null }),
            ),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        if (result.ok) {
          this.version.set(result.version);
          this.versionState.set('ready');
        } else if (!this.handleCommonFailure(result.error)) {
          this.versionState.set('error');
        }
      });

    if (!this.canRead()) {
      this.settle('forbidden');
    } else if (!QUOTE_ID_PATTERN.test(this.quoteId)) {
      this.settle('not-available');
    } else {
      this.loads.next();
    }
  }

  private load$(): Observable<LoadResult> {
    this.state.set('loading');
    return this.quotes.get(this.quoteId).pipe(
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
  }

  retryLoad(): void {
    this.loads.next();
  }

  private handleLoad(result: LoadResult): void {
    if (result.kind === 'unauthorized') {
      this.onUnauthorized();
      return;
    }
    if (result.kind === 'state') {
      this.settle(result.state);
      return;
    }
    this.apply(result.detail);
  }

  private apply(detail: QuoteDetail): void {
    // A quote that was never sent opens the editor for managers (BR-30).
    if (
      detail.sentVersions.length === 0 &&
      detail.draft !== null &&
      this.canManage() &&
      detail.status === 'draft'
    ) {
      void this.router.navigate(['/quotes', this.quoteId, 'edit'], { replaceUrl: true });
      return;
    }
    this.detail.set(detail);
    const current =
      detail.sentVersions.find((version) => version.isCurrent) ?? detail.sentVersions[0];
    this.version.set(null);
    this.selected.set(current?.versionNo ?? null);
    this.settle('ready');
    if (current !== undefined) {
      this.versionLoads.next(current.versionNo);
    }
  }

  private settle(state: PageState): void {
    this.state.set(state);
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }

  responseDate(response: QuoteResponse): string {
    return dateTimeAt(response.respondedAt, localZone());
  }

  money(value: number): string {
    return formatMoney(value, this.detail()?.organization.currency ?? null);
  }

  selectVersion(versionNo: number): void {
    this.selected.set(versionNo);
    this.versionLoads.next(versionNo);
  }

  retryVersion(): void {
    const selected = this.selected();
    if (selected !== null) {
      this.versionLoads.next(selected);
    }
  }

  // Actions (BR-26, BR-28)

  revise(): void {
    const detail = this.detail();
    if (detail === null || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.quotes
      .revise(this.quoteId, detail.updatedAt)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(false);
          void this.router.navigate(['/quotes', this.quoteId, 'edit']);
        },
        error: (error: unknown) => this.actionFailed(isApiError(error) ? error : null),
      });
  }

  openResend(): void {
    this.resendError.set(null);
    this.resendOpen.set(true);
  }

  resend(message: string): void {
    const detail = this.detail();
    if (detail === null || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.resendError.set(null);
    this.quotes
      .resendEmail(this.quoteId, detail.updatedAt, message)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result: SendResult) => {
          this.busy.set(false);
          this.resendOpen.set(false);
          this.detail.set(result.quote);
          this.messageService.add(
            result.emailStatus === 'failed'
              ? { severity: 'warn', summary: EMAIL_FAILED_MESSAGE }
              : { severity: 'success', summary: 'Quote email sent.' },
          );
        },
        error: (error: unknown) => this.actionFailed(isApiError(error) ? error : null, true),
      });
  }

  private handleCommonFailure(error: ApiError | null): boolean {
    switch (error?.kind) {
      case 'unauthorized':
        this.onUnauthorized();
        return true;
      case 'forbidden':
        this.settle('forbidden');
        return true;
      case 'not-found':
        this.settle('not-available');
        return true;
      default:
        return false;
    }
  }

  private actionFailed(error: ApiError | null, fromResend = false): void {
    this.busy.set(false);
    if (this.handleCommonFailure(error)) {
      return;
    }
    if (error?.kind === 'conflict') {
      if (error.code === 'no_draft' || error.code === 'quote_changed') {
        this.resendOpen.set(false);
        this.messageService.add({
          key: 'quote-changed',
          severity: 'warn',
          summary: QUOTE_CHANGED_MESSAGE,
          sticky: true,
        });
        return;
      }
    }
    const field = error?.fieldErrors['emailMessage']?.[0];
    if (fromResend && field !== undefined) {
      this.resendError.set(field);
      return;
    }
    this.messageService.add({ severity: 'error', summary: QUOTE_SAVE_FAILED_MESSAGE });
  }

  /** Refresh action of the `quote_changed` toast. */
  refresh(): void {
    this.messageService.clear('quote-changed');
    this.loads.next();
  }

  onUnauthorized(): void {
    handleUnauthorized(this.router, this.sessionExpired);
  }
}
