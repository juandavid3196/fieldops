import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  Calculation,
  DiscardResult,
  DraftBody,
  QuoteDetail,
  QuoteVersion,
  SendResult,
} from '../models/quote.model';
import {
  QuotesOptions,
  QuotesPage,
  QuotesRequest,
  QuotesSummary,
} from '../models/quotes-list.model';

/** `/quotes` endpoints (Final contract: specs/quote-builder/spec.md). Organization scope is server-side only. */
@Injectable({ providedIn: 'root' })
export class QuotesService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path = ''): string {
    return buildApiUrl(this.config, `quotes${path}`);
  }

  private item(id: string, path = ''): string {
    return this.url(`/${encodeURIComponent(id)}${path}`);
  }

  /** Quotes workspace (specs/quotes-management/spec.md, Final contract). */
  listOptions(): Observable<QuotesOptions> {
    return this.http.get<QuotesOptions>(this.url('/options'));
  }

  listSummary(branchId: string | null): Observable<QuotesSummary> {
    const params =
      branchId === null ? new HttpParams() : new HttpParams().set('branchId', branchId);
    return this.http.get<QuotesSummary>(this.url('/summary'), { params });
  }

  list(query: QuotesRequest): Observable<QuotesPage> {
    let params = new HttpParams()
      .set('tab', query.tab)
      .set('date', query.date)
      .set('sort', query.sort)
      .set('page', query.page)
      .set('pageSize', query.pageSize);
    for (const [key, value] of [
      ['q', query.q],
      ['branchId', query.branchId],
      ['status', query.status],
    ] as const) {
      if (value !== null && value !== '') {
        params = params.set(key, value);
      }
    }
    return this.http.get<QuotesPage>(this.url(), { params });
  }

  /** `201` for a new draft, `200` for the request's existing unsent quote. */
  create(requestId: string): Observable<QuoteDetail> {
    return this.http.post<QuoteDetail>(this.url(), { requestId });
  }

  get(id: string): Observable<QuoteDetail> {
    return this.http.get<QuoteDetail>(this.item(id));
  }

  version(id: string, versionNo: number): Observable<QuoteVersion> {
    return this.http.get<QuoteVersion>(this.item(id, `/versions/${versionNo}`));
  }

  /** Persists nothing; returns per-line amounts in request order, totals and margin. */
  calculate(id: string, body: DraftBody): Observable<Calculation> {
    return this.http.post<Calculation>(this.item(id, '/calculate'), body);
  }

  saveDraft(id: string, body: DraftBody, updatedAt: string): Observable<QuoteDetail> {
    return this.http.put<QuoteDetail>(this.item(id, '/draft'), { ...body, updatedAt });
  }

  send(
    id: string,
    body: DraftBody,
    updatedAt: string,
    emailMessage: string,
  ): Observable<SendResult> {
    return this.http.post<SendResult>(this.item(id, '/send'), {
      ...body,
      updatedAt,
      emailMessage,
    });
  }

  revise(id: string, updatedAt: string): Observable<QuoteDetail> {
    return this.http.post<QuoteDetail>(this.item(id, '/revise'), { updatedAt });
  }

  discardDraft(id: string, updatedAt: string): Observable<DiscardResult> {
    return this.http.post<DiscardResult>(this.item(id, '/discard-draft'), { updatedAt });
  }

  resendEmail(id: string, updatedAt: string, emailMessage: string): Observable<SendResult> {
    return this.http.post<SendResult>(this.item(id, '/resend-email'), { updatedAt, emailMessage });
  }
}
