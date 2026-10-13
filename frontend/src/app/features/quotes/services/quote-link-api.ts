import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { PublicQuote, PublicTotals } from '../models/public-quote.model';
import { PublicQuoteService } from './public-quote.service';

/**
 * Token-less contract of the customer quote page. The public link service implements it with the
 * link token; the portal service implements it with the session and the quote id. By default it
 * resolves to the public service, so public behavior is unchanged.
 */
@Injectable({ providedIn: 'root', useExisting: PublicQuoteService })
export abstract class QuoteLinkApi {
  abstract view(): Observable<PublicQuote>;
  abstract calculate(selectedOptionalLineIds: readonly string[]): Observable<PublicTotals>;
  abstract approve(selectedOptionalLineIds: readonly string[]): Observable<PublicQuote>;
  abstract decline(reason: string): Observable<PublicQuote>;
  abstract askQuestion(message: string): Observable<PublicQuote>;
  abstract photo(photoId: string): Observable<Blob>;
  abstract logo(): Observable<Blob>;
  abstract pdf(): Observable<Blob>;
}
