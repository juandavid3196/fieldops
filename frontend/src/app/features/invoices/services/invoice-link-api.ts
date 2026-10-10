import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import {
  AttemptState,
  BankTransferNotice,
  CardIntent,
  PublicInvoice,
  PublicPhoto,
} from '../models/invoice.model';
import { PublicInvoiceService } from './public-invoice.service';

/**
 * Token-less contract of the customer invoice page and payment flow. The public link service
 * implements it with the link token; the portal service implements it with the session and the
 * invoice id. By default it resolves to the public service, so public behavior is unchanged.
 */
@Injectable({ providedIn: 'root', useExisting: PublicInvoiceService })
export abstract class InvoiceLinkApi {
  abstract view(): Observable<PublicInvoice>;
  abstract logo(): Observable<Blob>;
  abstract pdf(): Observable<Blob>;
  abstract cardIntent(idempotencyKey: string): Observable<CardIntent>;
  abstract paymentStatus(attemptId: string): Observable<AttemptState>;
  abstract bankTransferNotice(idempotencyKey: string): Observable<BankTransferNotice>;
  abstract receipt(paymentId: string): Observable<Blob>;
  abstract completionReport(): Observable<Blob>;
  abstract photos(): Observable<readonly PublicPhoto[]>;
  abstract photoContent(photoId: string): Observable<Blob>;
  abstract submitReview(rating: number, comment: string): Observable<{ submitted: true }>;
  /** Path the card provider returns to after a redirect (BR-29). */
  abstract returnPath(): string;
}
