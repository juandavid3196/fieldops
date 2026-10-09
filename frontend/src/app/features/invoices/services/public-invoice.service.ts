import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  AttemptState,
  BankTransferNotice,
  CardIntent,
  PublicInvoice,
  PublicPhoto,
} from '../models/invoice.model';
import { PublicInvoiceTokenService } from './public-invoice-token.service';

/** Anonymous invoice link endpoints; the token travels only in JSON POST bodies (BR-28). */
@Injectable({ providedIn: 'root' })
export class PublicInvoiceService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly tokens = inject(PublicInvoiceTokenService);

  private url(path: string): string {
    return buildApiUrl(this.config, `public/invoice-links/${path}`);
  }

  private token(): string {
    return this.tokens.read() ?? '';
  }

  private body(): { token: string } {
    return { token: this.token() };
  }

  view(): Observable<PublicInvoice> {
    return this.http.post<PublicInvoice>(this.url('view'), this.body());
  }

  logo(): Observable<Blob> {
    return this.http.post(this.url('logo'), this.body(), { responseType: 'blob' });
  }

  pdf(): Observable<Blob> {
    return this.http.post(this.url('pdf'), this.body(), { responseType: 'blob' });
  }

  cardIntent(idempotencyKey: string): Observable<CardIntent> {
    return this.http.post<CardIntent>(this.url('payments/card-intent'), {
      token: this.token(),
      idempotencyKey,
    });
  }

  paymentStatus(attemptId: string): Observable<AttemptState> {
    return this.http.post<AttemptState>(this.url('payments/status'), {
      token: this.token(),
      attemptId,
    });
  }

  bankTransferNotice(idempotencyKey: string): Observable<BankTransferNotice> {
    return this.http.post<BankTransferNotice>(this.url('payments/bank-transfer-notice'), {
      token: this.token(),
      idempotencyKey,
    });
  }

  receipt(paymentId: string): Observable<Blob> {
    return this.http.post(
      this.url('receipt'),
      { token: this.token(), paymentId },
      { responseType: 'blob' },
    );
  }

  completionReport(): Observable<Blob> {
    return this.http.post(this.url('completion-report'), this.body(), { responseType: 'blob' });
  }

  photos(): Observable<readonly PublicPhoto[]> {
    return this.http.post<readonly PublicPhoto[]>(this.url('photos'), this.body());
  }

  photoContent(photoId: string): Observable<Blob> {
    return this.http.post(
      this.url('photos/content'),
      { token: this.token(), photoId },
      { responseType: 'blob' },
    );
  }

  submitReview(rating: number, comment: string): Observable<{ submitted: true }> {
    return this.http.post<{ submitted: true }>(this.url('review'), {
      token: this.token(),
      rating,
      comment,
    });
  }
}
