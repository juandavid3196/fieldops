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
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { Skeleton } from 'primeng/skeleton';
import { Observable } from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { PublicInvoiceSummary } from '../../components/public-invoice-summary/public-invoice-summary';
import { PublicNeutralState } from '../../components/public-neutral-state/public-neutral-state';
import { PublicPaymentPanel } from '../../components/public-payment-panel/public-payment-panel';
import { PublicPaymentSuccess } from '../../components/public-payment-success/public-payment-success';
import { PublicPhotoGallery } from '../../components/public-photo-gallery/public-photo-gallery';
import { PublicReviewCard } from '../../components/public-review-card/public-review-card';
import { PUBLIC_DOWNLOAD_FAILED_MESSAGE, PublicInvoice } from '../../models/invoice.model';
import { PublicInvoiceTokenService } from '../../services/public-invoice-token.service';
import { PublicInvoiceService } from '../../services/public-invoice.service';
import { PublicPaymentFlow } from '../../services/public-payment-flow';
import { downloadBlob, openBlob } from '../../utils/blob-download';
import { organizationInitials } from '../../utils/invoice-format';

type PageState = 'loading' | 'ready' | 'unavailable' | 'rate-limited' | 'error';
type Download = 'invoice' | 'receipt' | 'report';

const PUBLIC_PATH = '/invoices/view';

/**
 * Public customer invoice page (`/invoices/view`, BR-27 to BR-30): captures the link token from
 * the URL fragment, removes it and any Stripe return parameters from the address bar before any
 * request and sends the token only in POST bodies. It owns the view, the downloads and the
 * neutral states; the card flow lives in {@link PublicPaymentFlow}.
 */
@Component({
  selector: 'app-public-invoice',
  imports: [
    PublicInvoiceSummary,
    PublicNeutralState,
    PublicPaymentPanel,
    PublicPaymentSuccess,
    PublicPhotoGallery,
    PublicReviewCard,
    Skeleton,
  ],
  providers: [PublicPaymentFlow],
  templateUrl: './public-invoice.html',
  styleUrl: './public-invoice.scss',
})
export class PublicInvoicePage {
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly tokens = inject(PublicInvoiceTokenService);
  private readonly service = inject(PublicInvoiceService);
  private readonly flow = inject(PublicPaymentFlow);

  private logoObjectUrl: string | null = null;

  readonly state = signal<PageState>('loading');
  readonly invoice = signal<PublicInvoice | null>(null);
  readonly logoUrl = signal<string | null>(null);
  readonly busy = signal<Download | null>(null);
  readonly downloadError = signal<string | null>(null);
  readonly galleryOpen = signal(false);
  readonly initials = computed(() => organizationInitials(this.invoice()?.organization.name ?? ''));
  /** A paid invoice shows the success view instead of the invoice card and payment panel. */
  readonly closed = computed(() => this.invoice()?.status === 'paid');

  constructor() {
    this.destroyRef.onDestroy(() => this.releaseLogo());
    // BR-27/BR-29: capture first and replace the URL before any request. Replacing it also drops
    // every query parameter Stripe adds on return; none is read, sent, stored or logged.
    const snapshot = this.route.snapshot;
    this.tokens.captureFromFragment(snapshot.fragment);
    if (snapshot.fragment !== null || snapshot.queryParamMap.keys.length > 0) {
      this.location.replaceState(PUBLIC_PATH);
    }
    this.flow.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((event) => {
      if (event === 'unavailable') {
        this.show('unavailable');
      } else {
        this.load(false);
      }
    });
    this.load(true);
  }

  retry(): void {
    this.load(true);
  }

  /** Re-reads the invoice after a payment or a stale-state conflict, without the skeleton. */
  reload(): void {
    this.load(false);
  }

  unavailable(): void {
    this.show('unavailable');
  }

  private load(initial: boolean): void {
    if (this.tokens.read() === null) {
      this.show('unavailable');
      return;
    }
    if (initial) {
      this.state.set('loading');
    }
    this.service
      .view()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (invoice) => {
          this.invoice.set(invoice);
          this.downloadError.set(null);
          if (this.logoUrl() === null) {
            this.loadLogo(invoice);
          }
          if (this.flow.phase() === 'succeeded') {
            this.flow.reset();
          } else if (initial) {
            this.flow.resume(invoice);
          }
          this.show('ready', initial ? 'h1' : '#success-title');
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

  private show(state: PageState, focus = 'h1'): void {
    if (state === 'unavailable') {
      this.tokens.clear();
      this.invoice.set(null);
      this.releaseLogo();
    }
    this.state.set(state);
    afterNextRender(() => this.host.querySelector<HTMLElement>(focus)?.focus(), {
      injector: this.injector,
    });
  }

  private loadLogo(invoice: PublicInvoice): void {
    if (!invoice.organization.hasLogo) {
      return;
    }
    this.service
      .logo()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          this.releaseLogo();
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

  download(kind: Download): void {
    const invoice = this.invoice();
    if (invoice === null || this.busy() !== null) {
      return;
    }
    let request: Observable<Blob>;
    let save: (blob: Blob) => void;
    if (kind === 'invoice') {
      request = this.service.pdf();
      save = (blob) => downloadBlob(this.document, blob, `${invoice.number}.pdf`);
    } else if (kind === 'receipt') {
      const payment = invoice.payments.find((item) => item.receiptNumber !== null);
      if (payment === undefined) {
        return;
      }
      request = this.service.receipt(payment.paymentId);
      save = (blob) => downloadBlob(this.document, blob, `${payment.receiptNumber}.pdf`);
    } else {
      request = this.service.completionReport();
      const name = `${invoice.service.workOrderNumber}-completion-report.pdf`;
      save = (blob) => openBlob(this.document, blob, name);
    }
    this.busy.set(kind);
    this.downloadError.set(null);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (blob) => {
        save(blob);
        this.busy.set(null);
      },
      error: (error: unknown) => {
        this.busy.set(null);
        if (isApiError(error) && error.status === 404) {
          this.show('unavailable');
        } else {
          this.downloadError.set(PUBLIC_DOWNLOAD_FAILED_MESSAGE);
        }
      },
    });
  }
}
