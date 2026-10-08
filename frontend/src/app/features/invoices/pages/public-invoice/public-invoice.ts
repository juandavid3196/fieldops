import { DOCUMENT, Location } from '@angular/common';
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
import { ActivatedRoute } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Skeleton } from 'primeng/skeleton';

import { isApiError } from '../../../../core/models/api-error.model';
import { InvoicePreview } from '../../components/invoice-preview/invoice-preview';
import {
  LOAD_ERROR_MESSAGE,
  PUBLIC_PAYMENT_NOTE,
  PUBLIC_PDF_FAILED_MESSAGE,
  PUBLIC_RATE_LIMITED_MESSAGE,
  PUBLIC_UNAVAILABLE_MESSAGE,
  PublicInvoice,
} from '../../models/invoice.model';
import { PublicInvoiceTokenService } from '../../services/public-invoice-token.service';
import { PublicInvoiceService } from '../../services/public-invoice.service';
import { organizationInitials } from '../../utils/invoice-format';

type PageState = 'loading' | 'ready' | 'unavailable' | 'rate-limited' | 'error';
type PdfState = 'idle' | 'downloading' | 'failed';

const PUBLIC_PATH = '/invoices/view';

/**
 * Public customer invoice page (`/invoices/view`, BR-28): captures the link token from the URL
 * fragment, removes it from the address bar before any request and sends it only in POST bodies.
 * Strictly read-only: no inputs and no actions besides Download PDF.
 */
@Component({
  selector: 'app-public-invoice',
  imports: [ButtonDirective, InvoicePreview, Skeleton, SpinnerIcon],
  templateUrl: './public-invoice.html',
  styleUrl: './public-invoice.scss',
})
export class PublicInvoicePage {
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly tokens = inject(PublicInvoiceTokenService);
  private readonly service = inject(PublicInvoiceService);

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private logoObjectUrl: string | null = null;

  readonly unavailableMessage = PUBLIC_UNAVAILABLE_MESSAGE;
  readonly rateLimitedMessage = PUBLIC_RATE_LIMITED_MESSAGE;
  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
  readonly pdfErrorMessage = PUBLIC_PDF_FAILED_MESSAGE;
  readonly paymentNote = PUBLIC_PAYMENT_NOTE;

  readonly state = signal<PageState>('loading');
  readonly invoice = signal<PublicInvoice | null>(null);
  readonly logoUrl = signal<string | null>(null);
  readonly pdfState = signal<PdfState>('idle');
  readonly initials = computed(() => organizationInitials(this.invoice()?.organization.name ?? ''));

  constructor() {
    this.destroyRef.onDestroy(() => this.releaseLogo());
    // BR-28: capture first and replace the URL before any request; other fragments are ignored.
    const snapshot = this.route.snapshot;
    this.tokens.captureFromFragment(snapshot.fragment);
    if (snapshot.fragment !== null || snapshot.queryParamMap.keys.length > 0) {
      this.location.replaceState(PUBLIC_PATH);
    }
    this.load();
  }

  retry(): void {
    this.load();
  }

  private load(): void {
    if (this.tokens.read() === null) {
      this.show('unavailable');
      return;
    }
    this.state.set('loading');
    this.service
      .view()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (invoice) => {
          this.invoice.set(invoice);
          this.pdfState.set('idle');
          this.loadLogo(invoice);
          this.show('ready');
        },
        error: (error: unknown) => {
          const apiError = isApiError(error) ? error : null;
          if (apiError?.status === 404) {
            this.show('unavailable');
          } else {
            this.show(apiError?.kind === 'rate-limited' ? 'rate-limited' : 'error');
          }
        },
      });
  }

  private show(state: PageState): void {
    if (state === 'unavailable') {
      this.tokens.clear();
      this.invoice.set(null);
      this.releaseLogo();
    }
    this.state.set(state);
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }

  private loadLogo(invoice: PublicInvoice): void {
    this.releaseLogo();
    if (!invoice.organization.hasLogo) {
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

  downloadPdf(): void {
    const invoice = this.invoice();
    if (invoice === null || this.pdfState() === 'downloading') {
      return;
    }
    this.pdfState.set('downloading');
    this.service
      .pdf()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          const url = URL.createObjectURL(blob);
          const link = this.document.createElement('a');
          link.href = url;
          link.download = `${invoice.number}.pdf`;
          link.click();
          URL.revokeObjectURL(url);
          this.pdfState.set('idle');
        },
        error: (error: unknown) => {
          if (isApiError(error) && error.status === 404) {
            this.show('unavailable');
          } else {
            this.pdfState.set('failed');
          }
        },
      });
  }
}
