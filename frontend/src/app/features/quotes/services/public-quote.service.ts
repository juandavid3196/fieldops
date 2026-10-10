import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import { PublicQuote, PublicTotals } from '../models/public-quote.model';
import { PublicQuoteTokenService } from './public-quote-token.service';
import { QuoteLinkApi } from './quote-link-api';

/** Anonymous quote link endpoints; the token travels only in JSON POST bodies (BR-01). */
@Injectable({ providedIn: 'root' })
export class PublicQuoteService implements QuoteLinkApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly tokens = inject(PublicQuoteTokenService);

  private url(path: string): string {
    return buildApiUrl(this.config, `public/quote-links/${path}`);
  }

  private body<T extends object>(extra?: T): { token: string } & T {
    return { token: this.tokens.read() ?? '', ...(extra as T) };
  }

  view(): Observable<PublicQuote> {
    return this.http.post<PublicQuote>(this.url('view'), this.body());
  }

  calculate(selectedOptionalLineIds: readonly string[]): Observable<PublicTotals> {
    return this.http.post<PublicTotals>(
      this.url('calculate'),
      this.body({ selectedOptionalLineIds }),
    );
  }

  approve(selectedOptionalLineIds: readonly string[]): Observable<PublicQuote> {
    return this.http.post<PublicQuote>(
      this.url('approve'),
      this.body({ selectedOptionalLineIds, acceptTerms: true }),
    );
  }

  decline(reason: string): Observable<PublicQuote> {
    return this.http.post<PublicQuote>(this.url('decline'), this.body({ reason }));
  }

  askQuestion(message: string): Observable<PublicQuote> {
    return this.http.post<PublicQuote>(this.url('clarification'), this.body({ message }));
  }

  photo(photoId: string): Observable<Blob> {
    return this.http.post(this.url(`photos/${encodeURIComponent(photoId)}`), this.body(), {
      responseType: 'blob',
    });
  }

  logo(): Observable<Blob> {
    return this.http.post(this.url('logo'), this.body(), { responseType: 'blob' });
  }

  pdf(): Observable<Blob> {
    return this.http.post(this.url('pdf'), this.body(), { responseType: 'blob' });
  }
}
