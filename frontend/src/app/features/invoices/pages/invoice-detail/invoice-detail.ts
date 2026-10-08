import { DOCUMENT } from '@angular/common';
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
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import { Observable, Subject, catchError, map, of, switchMap } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import {
  dueDate as dueDateFor,
  formatCalendarDate,
  formatInstantDate,
  formatInstantTime,
  money,
} from '../../../billing-review/utils/billing-review-format';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { InvoicePreview } from '../../components/invoice-preview/invoice-preview';
import {
  InvoiceAction,
  InvoiceSendPanel,
} from '../../components/invoice-send-panel/invoice-send-panel';
import {
  ACTION_FAILED_MESSAGE,
  CONFLICT_MESSAGES,
  DeliveryErrors,
  DeliveryValues,
  EMAIL_FAILED_MESSAGE,
  FORBIDDEN_MESSAGE,
  INVOICE_READ_ROLES,
  InvoiceDetail,
  LOAD_ERROR_MESSAGE,
  PDF_FAILED_MESSAGE,
  READ_ONLY_MESSAGE,
  UNAVAILABLE_MESSAGE,
} from '../../models/invoice.model';
import { InvoiceService } from '../../services/invoice.service';
import {
  DELIVERY_FIELD_ORDER,
  firstInvalid,
  initialValues,
  sameValues,
  serverErrors,
  statusLabel,
  toBody,
  validateDelivery,
} from '../../utils/invoice-format';

type PageState = 'loading' | 'ready' | 'forbidden' | 'not-available' | 'error';
type LoadResult =
  | { readonly kind: 'ready'; readonly invoice: InvoiceDetail }
  | { readonly kind: 'state'; readonly state: PageState }
  | { readonly kind: 'unauthorized' };

const SEND_KEY = 'send-invoice';

/**
 * Invoice page (`/invoices/:invoiceId`, Design 12): preview, delivery panel, save, send, resend
 * and PDF. Technicians and unknown roles are forbidden with no calls; `canAct` only decides what
 * is shown, the backend authorizes every action.
 */
@Component({
  selector: 'app-invoice-detail',
  imports: [
    ButtonDirective,
    ConfirmDialog,
    DiscardChangesDialog,
    InvoicePreview,
    InvoiceSendPanel,
    Message,
    RouterLink,
    Skeleton,
    SpinnerIcon,
    Toast,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './invoice-detail.html',
  styleUrl: './invoice-detail.scss',
})
export class InvoiceDetailPage {
  private readonly api = inject(InvoiceService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(MessageService);
  private readonly confirmations = inject(ConfirmationService);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly panel = viewChild(InvoiceSendPanel);
  private readonly loads = new Subject<void>();

  readonly invoiceId = this.route.snapshot.paramMap.get('invoiceId') ?? '';
  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly unavailableMessage = UNAVAILABLE_MESSAGE;
  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
  readonly sendKey = SEND_KEY;

  readonly sessionExpired = signal(false);
  readonly state = signal<PageState>('loading');
  readonly invoice = signal<InvoiceDetail | null>(null);
  readonly baseline = signal<DeliveryValues | null>(null);
  readonly values = signal<DeliveryValues>({ paymentTerms: null, recipientEmail: '', message: '' });
  readonly errors = signal<DeliveryErrors>({});
  readonly pending = signal<InvoiceAction | null>(null);
  /** Polite live region text (BR-26). */
  readonly announcement = signal('');

  private readonly roleCode = computed(() => this.sessionService.session()?.role.code ?? '');

  readonly isDraft = computed(() => this.invoice()?.status === 'draft');
  /** Save and Send exist only for a draft the user may act on (BR-25). */
  readonly canEdit = computed(() => this.isDraft() && this.invoice()?.canAct === true);
  readonly dirty = computed(() => {
    const baseline = this.baseline();
    return this.canEdit() && baseline !== null && !sameValues(this.values(), baseline);
  });
  readonly statusText = computed(() => statusLabel(this.invoice()?.status ?? ''));
  readonly dueDate = computed(() => {
    const invoice = this.invoice();
    const terms = this.values().paymentTerms;
    if (invoice === null) {
      return '';
    }
    return this.isDraft() && terms !== null
      ? dueDateFor(invoice.issueDate, terms)
      : invoice.dueDate;
  });
  readonly dates = computed(() => {
    const invoice = this.invoice();
    return invoice === null
      ? null
      : { issue: formatCalendarDate(invoice.issueDate), due: formatCalendarDate(this.dueDate()) };
  });
  /** Activity footer (BR-03): the state text and who created the invoice and when. */
  readonly activity = computed(() => {
    const invoice = this.invoice();
    if (invoice === null) {
      return null;
    }
    const zone = invoice.timezone;
    const when = (iso: string): string =>
      `${formatInstantDate(iso, zone)} at ${formatInstantTime(iso, zone)}`;
    const created = `Invoice created from completed job ${invoice.workOrderNumber}`;
    const recipient = invoice.delivery.recipientEmail;
    return {
      text:
        invoice.sentAt === null
          ? `${created} · Not yet sent`
          : `Sent ${when(invoice.sentAt)}${recipient ? ` to ${recipient}` : ''}`,
      meta: `${when(invoice.createdAt)} · by ${invoice.createdByName}`,
    };
  });

  constructor() {
    this.loads
      .pipe(
        switchMap(() => this.load$()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handleLoad(result));

    if (!INVOICE_READ_ROLES.includes(this.roleCode())) {
      this.settle('forbidden');
    } else {
      this.loads.next();
    }
  }

  retry(): void {
    this.loads.next();
  }

  private load$(): Observable<LoadResult> {
    this.state.set('loading');
    return this.api.get(this.invoiceId).pipe(
      map((invoice): LoadResult => ({ kind: 'ready', invoice })),
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

  private handleLoad(result: LoadResult): void {
    if (result.kind === 'unauthorized') {
      handleUnauthorized(this.router, this.sessionExpired);
    } else if (result.kind === 'state') {
      this.settle(result.state);
    } else {
      this.apply(result.invoice);
      this.settle('ready');
    }
  }

  private apply(invoice: InvoiceDetail): void {
    const values = initialValues(invoice);
    this.invoice.set(invoice);
    this.baseline.set(values);
    this.values.set(values);
    this.errors.set({});
  }

  private settle(state: PageState): void {
    this.state.set(state);
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }

  // Editing

  changeValues(values: DeliveryValues): void {
    this.values.set(values);
    const shown = this.errors();
    if (firstInvalid(shown) !== null) {
      // Fields that already show an error are re-checked live: fixed ones clear, others update.
      const current = validateDelivery(values, 'send');
      const next: DeliveryErrors = {};
      for (const field of DELIVERY_FIELD_ORDER) {
        if (shown[field] !== undefined && current[field] !== undefined) {
          next[field] = current[field];
        }
      }
      this.errors.set(next);
    }
  }

  // Save (BR-09, BR-26)

  save(): void {
    const invoice = this.invoice();
    if (invoice === null || this.pending() !== null || !this.dirty()) {
      return;
    }
    const errors = validateDelivery(this.values(), 'save');
    const body = toBody(this.values(), invoice.updatedAt);
    if (body === null || firstInvalid(errors) !== null) {
      this.invalid(errors);
      return;
    }
    this.pending.set('save');
    this.run(
      this.api.saveDraft(invoice.id, body),
      (updated) => {
        this.apply(updated);
        this.notify('success', 'Draft saved.');
      },
      'save',
    );
  }

  // Send (BR-13, BR-14, BR-26)

  requestSend(): void {
    const invoice = this.invoice();
    if (invoice === null || this.pending() !== null || !this.canEdit()) {
      return;
    }
    const errors = validateDelivery(this.values(), 'send');
    if (firstInvalid(errors) !== null) {
      this.invalid(errors);
      return;
    }
    this.errors.set({});
    const recipient = this.values().recipientEmail.trim();
    this.confirmations.confirm({
      key: SEND_KEY,
      header: 'Send invoice?',
      message: `${invoice.customerName} will receive ${invoice.number} for ${money(invoice.totals.total, invoice.currency)} at ${recipient}. A sent invoice can't be edited.`,
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Send invoice' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () => this.send(),
    });
  }

  private send(): void {
    const invoice = this.invoice();
    const body = invoice === null ? null : toBody(this.values(), invoice.updatedAt);
    if (invoice === null || body === null || this.pending() !== null) {
      return;
    }
    this.pending.set('send');
    this.run(this.api.send(invoice.id, body), (result) => {
      const number = result.invoice.number;
      if (!result.changed) {
        this.notify('info', `Invoice ${number} was already sent.`);
        this.loads.next();
        return;
      }
      this.apply(result.invoice);
      this.notifyEmail(result.emailStatus, `Invoice ${number} sent.`);
    });
  }

  // Resend (BR-17)

  resend(): void {
    const invoice = this.invoice();
    if (invoice === null || this.pending() !== null) {
      return;
    }
    this.pending.set('resend');
    this.run(this.api.resendEmail(invoice.id, invoice.updatedAt), (result) => {
      this.apply(result.invoice);
      this.notifyEmail(result.emailStatus, 'Invoice email sent again.');
    });
  }

  // PDF (BR-26)

  downloadPdf(): void {
    const invoice = this.invoice();
    if (invoice === null || this.pending() !== null) {
      return;
    }
    this.pending.set('pdf');
    this.api
      .pdf(invoice.id, `${invoice.number}.pdf`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (file) => {
          this.pending.set(null);
          const url = URL.createObjectURL(file.blob);
          const link = this.document.createElement('a');
          link.href = url;
          link.download = file.fileName;
          link.click();
          URL.revokeObjectURL(url);
        },
        error: (error: unknown) => {
          this.pending.set(null);
          if (isApiError(error) && error.kind === 'unauthorized') {
            handleUnauthorized(this.router, this.sessionExpired);
          } else {
            this.notify('error', PDF_FAILED_MESSAGE);
          }
        },
      });
  }

  // Outcomes

  private run<T>(
    request: Observable<T>,
    onSuccess: (value: T) => void,
    mode: 'save' | 'send' = 'send',
  ): void {
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (value) => {
        this.pending.set(null);
        onSuccess(value);
      },
      error: (error: unknown) => {
        this.pending.set(null);
        this.failed(isApiError(error) ? error : null, mode);
      },
    });
  }

  private failed(error: ApiError | null, mode: 'save' | 'send'): void {
    const code = error?.code ?? '';
    switch (error?.kind) {
      case 'unauthorized':
        handleUnauthorized(this.router, this.sessionExpired);
        return;
      case 'forbidden':
        this.notify('error', READ_ONLY_MESSAGE);
        return;
      case 'not-found':
        this.notify('error', UNAVAILABLE_MESSAGE);
        this.loads.next();
        return;
      case 'conflict':
        if (code in CONFLICT_MESSAGES) {
          this.notify('error', CONFLICT_MESSAGES[code]);
          this.loads.next();
          return;
        }
        break;
      case 'validation': {
        const next = serverErrors(Object.keys(error.fieldErrors), this.values(), mode);
        if (firstInvalid(next) !== null) {
          this.invalid(next);
          return;
        }
        break;
      }
    }
    this.notify('error', ACTION_FAILED_MESSAGE);
  }

  private invalid(errors: DeliveryErrors): void {
    this.errors.set(errors);
    const field = firstInvalid(errors);
    if (field !== null) {
      afterNextRender(() => this.panel()?.focusField(field), { injector: this.injector });
    }
  }

  private notifyEmail(status: string, success: string): void {
    if (status === 'failed') {
      this.notify('warn', EMAIL_FAILED_MESSAGE);
    } else {
      this.notify('success', success);
    }
  }

  private notify(severity: 'success' | 'info' | 'warn' | 'error', summary: string): void {
    this.messages.add({ severity, summary });
    this.announcement.set(summary);
  }

  /** Consulted by `invoiceUnsavedChangesGuard` on route leave (BR-26). */
  canLeave(): boolean | Observable<boolean> {
    if (this.sessionExpired() || !this.dirty()) {
      return true;
    }
    return new Observable<boolean>((subscriber) => {
      this.confirmations.confirm(
        discardChangesConfirmation({
          subject: 'this invoice',
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
