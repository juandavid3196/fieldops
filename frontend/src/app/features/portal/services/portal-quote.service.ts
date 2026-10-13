import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Observable } from 'rxjs';

import { API_CONFIG } from '../../../core/config/api.config';
import { PublicQuote, PublicTotals } from '../../quotes/models/public-quote.model';
import { QuoteLinkApi } from '../../quotes/services/quote-link-api';
import { Page, QuoteRow } from '../models/portal.model';
import { encode, listParams, portalUrl } from './portal-url';

/** Quotes list and the explicit-id question used by the dashboard card (BR-20, BR-30). */
@Injectable({ providedIn: 'root' })
export class PortalQuotesService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  list(page: number, propertyId: string | null): Observable<Page<QuoteRow>> {
    return this.http.get<Page<QuoteRow>>(portalUrl(this.config, 'quotes'), {
      params: listParams(page, propertyId),
    });
  }

  askQuestion(quoteId: string, message: string): Observable<PublicQuote> {
    return this.http.post<PublicQuote>(
      portalUrl(this.config, `quotes/${encode(quoteId)}/clarification`),
      { message },
    );
  }
}

/**
 * Portal implementation of the quote page contract: authorized by the session and the quote id of
 * the wrapper route (`quotes/:quoteId`), never by a token. Provided by the wrapper page.
 */
@Injectable()
export class PortalQuoteService implements QuoteLinkApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly route = inject(ActivatedRoute);

  private url(path = ''): string {
    const id = this.route.snapshot.paramMap.get('quoteId') ?? '';
    return portalUrl(this.config, `quotes/${encode(id)}${path}`);
  }

  view(): Observable<PublicQuote> {
    return this.http.get<PublicQuote>(this.url());
  }

  calculate(selectedOptionalLineIds: readonly string[]): Observable<PublicTotals> {
    return this.http.post<PublicTotals>(this.url('/calculate'), { selectedOptionalLineIds });
  }

  approve(selectedOptionalLineIds: readonly string[]): Observable<PublicQuote> {
    return this.http.post<PublicQuote>(this.url('/approve'), {
      selectedOptionalLineIds,
      acceptTerms: true,
    });
  }

  decline(reason: string): Observable<PublicQuote> {
    return this.http.post<PublicQuote>(this.url('/reject'), { reason });
  }

  askQuestion(message: string): Observable<PublicQuote> {
    return this.http.post<PublicQuote>(this.url('/clarification'), { message });
  }

  photo(photoId: string): Observable<Blob> {
    return this.http.get(this.url(`/photos/${encode(photoId)}/content`), { responseType: 'blob' });
  }

  logo(): Observable<Blob> {
    return this.http.get(portalUrl(this.config, 'organization/logo'), { responseType: 'blob' });
  }

  pdf(): Observable<Blob> {
    return this.http.get(this.url('/pdf'), { responseType: 'blob' });
  }
}
