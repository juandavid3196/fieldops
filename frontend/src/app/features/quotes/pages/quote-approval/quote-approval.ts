import { DOCUMENT, Location } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { CheckIcon } from 'primeng/icons/check';
import { ExclamationTriangleIcon } from 'primeng/icons/exclamationtriangle';
import { InfoCircleIcon } from 'primeng/icons/infocircle';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { TimesCircleIcon } from 'primeng/icons/timescircle';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import { Subject, catchError, debounceTime, map, of, switchMap } from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { PublicQuoteDetails } from '../../components/public-quote-details/public-quote-details';
import {
  PublicQuoteDialog,
  PublicQuoteDialogKind,
  publicTextError,
} from '../../components/public-quote-dialog/public-quote-dialog';
import {
  OptionalToggle,
  PublicQuotePrice,
} from '../../components/public-quote-price/public-quote-price';
import { PublicQuoteProgress } from '../../components/public-quote-progress/public-quote-progress';
import {
  PdfState,
  PublicQuoteSummary,
} from '../../components/public-quote-summary/public-quote-summary';
import {
  ACCEPT_TERMS_ERROR,
  ALREADY_ANSWERED,
  CALCULATE_ERROR,
  OPTIONAL_SELECTION_ERROR,
  PDF_ERROR,
  PUBLIC_LOAD_ERROR,
  PUBLIC_RATE_LIMITED,
  PublicQuote,
  PublicQuoteStatus,
  PublicTotals,
  QUOTE_EXPIRED_NOTICE,
  RESPONSE_ERROR,
  SUPERSEDED_BODY,
  SUPERSEDED_TITLE,
  UNAVAILABLE_BODY,
  UNAVAILABLE_TITLE,
} from '../../models/public-quote.model';
import { PublicQuoteTokenService } from '../../services/public-quote-token.service';
import { QuoteLinkApi } from '../../services/quote-link-api';
import { customerInitials, dateOnlyLabel } from '../../utils/quote-format';

type PageState = 'loading' | 'ready' | 'unavailable' | 'superseded' | 'rate-limited' | 'error';
type ChipTone = 'warning' | 'info' | 'success' | 'neutral';

const PUBLIC_PATH = '/quotes/view';
const CALCULATE_DEBOUNCE_MS = 250;

const CHIPS: Readonly<Record<PublicQuoteStatus, { label: string; tone: ChipTone }>> = {
  sent: { label: 'Awaiting your approval', tone: 'warning' },
  expired: { label: 'Expired', tone: 'neutral' },
  clarification_requested: { label: 'Question sent', tone: 'info' },
  approved: { label: 'Approved', tone: 'success' },
  rejected: { label: 'Declined', tone: 'neutral' },
};

type CalculateResult =
  | { readonly ok: true; readonly ids: readonly string[]; readonly totals: PublicTotals }
  | { readonly ok: false; readonly error: ApiError | null };

interface DialogSubmission {
  readonly kind: PublicQuoteDialogKind;
  readonly text: string;
}

/**
 * Public customer quote page (`/quotes/view`, customer-quote-approval): captures the link token
 * from the URL fragment, removes it from the address bar before any request and sends it only in
 * POST bodies. Totals always come from the server.
 */
@Component({
  selector: 'app-quote-approval',
  imports: [
    ButtonDirective,
    CheckIcon,
    ExclamationTriangleIcon,
    InfoCircleIcon,
    Skeleton,
    SpinnerIcon,
    TimesCircleIcon,
    Toast,
    RouterLink,
    PublicQuoteDetails,
    PublicQuoteDialog,
    PublicQuotePrice,
    PublicQuoteProgress,
    PublicQuoteSummary,
  ],
  providers: [MessageService],
  host: { '[class.embedded]': 'embedded()' },
  templateUrl: './quote-approval.html',
  styleUrl: './quote-approval.scss',
})
export class QuoteApproval implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly messages = inject(MessageService);
  private readonly tokens = inject(PublicQuoteTokenService);
  private readonly service = inject(QuoteLinkApi);

  /** Rendered inside the portal shell: session-authorized, no token, header and footer hidden. */
  readonly embedded = input(false);
  /** Embedded only: the quote is not available (`404`); the portal page owns that state. */
  readonly missing = output<void>();

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly calculations = new Subject<readonly string[]>();
  private logoObjectUrl: string | null = null;
  private trigger: HTMLElement | null = null;
  private confirmedIds: readonly string[] = [];

  readonly unavailableTitle = UNAVAILABLE_TITLE;
  readonly unavailableBody = UNAVAILABLE_BODY;
  readonly supersededTitle = SUPERSEDED_TITLE;
  readonly supersededBody = SUPERSEDED_BODY;
  readonly loadError = PUBLIC_LOAD_ERROR;
  readonly rateLimited = PUBLIC_RATE_LIMITED;
  readonly pdfError = PDF_ERROR;
  readonly year = new Date().getFullYear();

  readonly state = signal<PageState>('loading');
  readonly quote = signal<PublicQuote | null>(null);
  readonly totals = signal<PublicTotals | null>(null);
  readonly selectedIds = signal<readonly string[]>([]);
  readonly calculating = signal(false);
  readonly calculateError = signal<string | null>(null);
  readonly acceptTerms = signal(false);
  readonly submitting = signal(false);
  readonly actionError = signal<string | null>(null);
  readonly dialog = signal<PublicQuoteDialogKind | null>(null);
  readonly dialogFieldError = signal<string | null>(null);
  readonly dialogFailure = signal<string | null>(null);
  readonly pdfState = signal<PdfState>('idle');
  readonly logoUrl = signal<string | null>(null);

  readonly expiredNotice = computed(() =>
    QUOTE_EXPIRED_NOTICE(this.quote()?.organization.name ?? 'the company'),
  );
  readonly editable = computed(() => {
    const status = this.quote()?.quote.status;
    return status === 'sent' || status === 'clarification_requested';
  });
  readonly chip = computed(() => {
    const status = this.quote()?.quote.status;
    return status === undefined ? null : CHIPS[status];
  });
  readonly initials = computed(() => customerInitials(this.quote()?.organization.name ?? ''));
  readonly sentOn = computed(() => dateOnlyLabel(this.quote()?.quote.sentOn ?? ''));
  readonly validUntil = computed(() => dateOnlyLabel(this.quote()?.quote.validUntil ?? ''));

  constructor() {
    this.destroyRef.onDestroy(() => this.releaseLogo());

    this.calculations
      .pipe(
        debounceTime(CALCULATE_DEBOUNCE_MS),
        switchMap((ids) =>
          this.service.calculate(ids).pipe(
            map((totals): CalculateResult => ({ ok: true, ids, totals })),
            catchError((error: unknown) =>
              of<CalculateResult>({ ok: false, error: isApiError(error) ? error : null }),
            ),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.applyCalculation(result));
  }

  ngOnInit(): void {
    if (!this.embedded()) {
      // BR-01: capture first and replace the URL before any request; other fragments are ignored.
      const snapshot = this.route.snapshot;
      this.tokens.captureFromFragment(snapshot.fragment);
      if (snapshot.fragment !== null || snapshot.queryParamMap.keys.length > 0) {
        this.location.replaceState(PUBLIC_PATH);
      }
    }
    this.load();
  }

  retry(): void {
    this.load();
  }

  // Loading

  private load(): void {
    if (!this.embedded() && this.tokens.read() === null) {
      this.show('unavailable');
      return;
    }
    this.state.set('loading');
    this.service
      .view()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (quote) => this.apply(quote),
        error: (error: unknown) => this.loadFailed(error),
      });
  }

  private apply(quote: PublicQuote): void {
    const response = quote.response;
    const selected = response?.selectedOptionalLineIds ?? [];
    this.quote.set(quote);
    this.totals.set(response?.totals ?? quote.versionTotals);
    this.selectedIds.set(selected);
    this.confirmedIds = selected;
    this.calculating.set(false);
    this.calculateError.set(null);
    this.actionError.set(null);
    this.acceptTerms.set(false);
    this.pdfState.set('idle');
    this.loadLogo(quote);
    this.show('ready');
  }

  private loadFailed(error: unknown): void {
    const apiError = isApiError(error) ? error : null;
    if (apiError?.status === 404) {
      this.show('unavailable');
    } else if (apiError?.status === 410) {
      this.show('superseded');
    } else {
      this.show(apiError?.kind === 'rate-limited' ? 'rate-limited' : 'error');
    }
  }

  private show(state: PageState): void {
    if (state === 'unavailable' || state === 'superseded') {
      if (this.embedded()) {
        if (state === 'unavailable') {
          this.missing.emit();
        }
      } else {
        this.tokens.clear();
      }
      this.quote.set(null);
      this.dialog.set(null);
      this.releaseLogo();
    }
    this.state.set(state);
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }

  private loadLogo(quote: PublicQuote): void {
    this.releaseLogo();
    if (!quote.organization.hasLogo) {
      return;
    }
    this.service
      .logo()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          this.logoObjectUrl = URL.createObjectURL(blob);
          this.logoUrl.set(this.logoObjectUrl);
        },
        error: () => this.logoUrl.set(null),
      });
  }

  private releaseLogo(): void {
    if (this.logoObjectUrl !== null) {
      URL.revokeObjectURL(this.logoObjectUrl);
      this.logoObjectUrl = null;
    }
    this.logoUrl.set(null);
  }

  // Optional items (BR-11)

  toggleOptional(toggle: OptionalToggle): void {
    if (!this.editable() || this.submitting()) {
      return;
    }
    const others = this.selectedIds().filter((id) => id !== toggle.id);
    const ids = toggle.checked ? [...others, toggle.id] : others;
    this.selectedIds.set(ids);
    this.calculateError.set(null);
    this.calculating.set(true);
    this.calculations.next(ids);
  }

  private applyCalculation(result: CalculateResult): void {
    this.calculating.set(false);
    if (result.ok) {
      this.confirmedIds = result.ids;
      this.totals.set(result.totals);
      return;
    }
    this.selectedIds.set(this.confirmedIds);
    const status = result.error?.status;
    if (status === 404 || status === 410) {
      this.show(status === 404 ? 'unavailable' : 'superseded');
    } else {
      this.calculateError.set(
        result.error?.kind === 'rate-limited' ? PUBLIC_RATE_LIMITED : CALCULATE_ERROR,
      );
    }
  }

  // Responses (BR-12, BR-14, BR-15, BR-16)

  approve(): void {
    if (!this.acceptTerms() || this.calculating() || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.actionError.set(null);
    this.service
      .approve(this.selectedIds())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (quote) => this.answered(quote),
        error: (error: unknown) => this.actionFailed(error, null),
      });
  }

  openDialog(kind: PublicQuoteDialogKind): void {
    const active = this.document.activeElement;
    this.trigger = active instanceof HTMLElement ? active : null;
    this.dialogFieldError.set(null);
    this.dialogFailure.set(null);
    this.dialog.set(kind);
  }

  closeDialog(): void {
    if (this.submitting()) {
      return;
    }
    this.dialog.set(null);
    const trigger = this.trigger;
    this.trigger = null;
    afterNextRender(() => trigger?.focus(), { injector: this.injector });
  }

  submitDialog(text: string): void {
    const kind = this.dialog();
    if (kind === null || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.dialogFieldError.set(null);
    this.dialogFailure.set(null);
    (kind === 'decline' ? this.service.decline(text) : this.service.askQuestion(text))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (quote) => {
          this.dialog.set(null);
          this.trigger = null;
          this.answered(quote);
        },
        error: (error: unknown) => this.actionFailed(error, { kind, text }),
      });
  }

  private answered(quote: PublicQuote): void {
    this.submitting.set(false);
    this.apply(quote);
  }

  private actionFailed(error: unknown, dialog: DialogSubmission | null): void {
    this.submitting.set(false);
    const apiError = isApiError(error) ? error : null;
    switch (apiError?.status) {
      case 404:
        this.show('unavailable');
        return;
      case 410:
        this.show('superseded');
        return;
      case 409:
        // BR-16: someone already answered; reload to the final state. An expired quote (portal)
        // reloads to its read-only expired state without the answered notice.
        this.dialog.set(null);
        if (apiError.code !== 'quote_expired') {
          this.messages.add({ severity: 'info', summary: ALREADY_ANSWERED });
        }
        this.load();
        return;
    }

    if (apiError?.kind === 'rate-limited') {
      this.report(PUBLIC_RATE_LIMITED, dialog);
    } else if (apiError?.kind === 'validation' && dialog !== null) {
      this.dialogFieldError.set(publicTextError(dialog.kind, dialog.text) ?? RESPONSE_ERROR);
    } else if (apiError?.kind === 'validation') {
      this.actionError.set(
        apiError.fieldErrors['acceptTerms'] !== undefined
          ? ACCEPT_TERMS_ERROR
          : OPTIONAL_SELECTION_ERROR,
      );
    } else {
      this.report(RESPONSE_ERROR, dialog);
    }
  }

  private report(message: string, dialog: DialogSubmission | null): void {
    if (dialog === null) {
      this.actionError.set(message);
    } else {
      this.dialogFailure.set(message);
    }
  }

  // PDF (BR-19)

  downloadPdf(): void {
    const quote = this.quote();
    if (quote === null || this.pdfState() === 'downloading') {
      return;
    }
    this.pdfState.set('downloading');
    this.service
      .pdf()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          this.save(blob, `${quote.quote.displayNumber}-v${quote.quote.versionNo}.pdf`);
          this.pdfState.set('idle');
        },
        error: (error: unknown) => {
          const status = isApiError(error) ? error.status : 0;
          if (status === 404 || status === 410) {
            this.show(status === 404 ? 'unavailable' : 'superseded');
          } else {
            this.pdfState.set('failed');
          }
        },
      });
  }

  private save(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
  }
}
