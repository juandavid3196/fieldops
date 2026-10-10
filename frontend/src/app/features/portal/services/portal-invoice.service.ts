import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Observable } from 'rxjs';

import { API_CONFIG } from '../../../core/config/api.config';
import {
  AttemptState,
  BankTransferNotice,
  CardIntent,
  PublicInvoice,
  PublicPhoto,
} from '../../invoices/models/invoice.model';
import { InvoiceLinkApi } from '../../invoices/services/invoice-link-api';
import { InvoiceRow, Page } from '../models/portal.model';
import { encode, listParams, portalUrl } from './portal-url';

/** Invoices list and the receipt of a dashboard activity row (BR-26, BR-31). */
@Injectable({ providedIn: 'root' })
export class PortalInvoicesService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  list(page: number, propertyId: string | null): Observable<Page<InvoiceRow>> {
    return this.http.get<Page<InvoiceRow>>(portalUrl(this.config, 'invoices'), {
      params: listParams(page, propertyId),
    });
  }

  receipt(invoiceId: string, paymentId: string): Observable<Blob> {
    return this.http.get(
      portalUrl(this.config, `invoices/${encode(invoiceId)}/payments/${encode(paymentId)}/receipt`),
      { responseType: 'blob' },
    );
  }
}

/**
 * Portal implementation of the invoice page contract: authorized by the session and the invoice id
 * of the wrapper route (`invoices/:invoiceId`), never by a token. Provided by the wrapper page.
 */
@Injectable()
export class PortalInvoiceService implements InvoiceLinkApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly route = inject(ActivatedRoute);

  private id(): string {
    return this.route.snapshot.paramMap.get('invoiceId') ?? '';
  }

  private url(path = ''): string {
    return portalUrl(this.config, `invoices/${encode(this.id())}${path}`);
  }

  returnPath(): string {
    return `/portal/invoices/${encode(this.id())}`;
  }

  view(): Observable<PublicInvoice> {
    return this.http.get<PublicInvoice>(this.url());
  }

  logo(): Observable<Blob> {
    return this.http.get(portalUrl(this.config, 'organization/logo'), { responseType: 'blob' });
  }

  pdf(): Observable<Blob> {
    return this.http.get(this.url('/pdf'), { responseType: 'blob' });
  }

  cardIntent(idempotencyKey: string): Observable<CardIntent> {
    return this.http.post<CardIntent>(this.url('/payment-attempts'), { idempotencyKey });
  }

  paymentStatus(attemptId: string): Observable<AttemptState> {
    return this.http.get<AttemptState>(this.url(`/payment-attempts/${encode(attemptId)}`));
  }

  bankTransferNotice(idempotencyKey: string): Observable<BankTransferNotice> {
    return this.http.post<BankTransferNotice>(this.url('/bank-transfer-notice'), {
      idempotencyKey,
    });
  }

  receipt(paymentId: string): Observable<Blob> {
    return this.http.get(this.url(`/payments/${encode(paymentId)}/receipt`), {
      responseType: 'blob',
    });
  }

  completionReport(): Observable<Blob> {
    return this.http.get(this.url('/completion-report'), { responseType: 'blob' });
  }

  photos(): Observable<readonly PublicPhoto[]> {
    return this.http.get<readonly PublicPhoto[]>(this.url('/photos'));
  }

  photoContent(photoId: string): Observable<Blob> {
    return this.http.get(this.url(`/photos/${encode(photoId)}/content`), { responseType: 'blob' });
  }

  submitReview(rating: number, comment: string): Observable<{ submitted: true }> {
    return this.http.post<{ submitted: true }>(this.url('/review'), { rating, comment });
  }
}
