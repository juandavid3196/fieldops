import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  BillingReviewDetail,
  BillingReviewOptions,
  GenerateInvoiceBody,
  GeneratedInvoice,
  QueueFilters,
  QueueResponse,
  ReviewState,
  ReviewUpdate,
} from '../models/billing-review.model';

/** A downloaded CSV: the server's file name (when exposed) or the client fallback. */
export interface ExportFile {
  readonly blob: Blob;
  readonly fileName: string;
}

/** Completed jobs review endpoints (Final contract: specs/completed-jobs-review/spec.md). */
@Injectable({ providedIn: 'root' })
export class BillingReviewService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path: string): string {
    return buildApiUrl(this.config, `billing-review/${path}`);
  }

  private orderUrl(workOrderId: string, path = ''): string {
    return this.url(`work-orders/${encodeURIComponent(workOrderId)}${path}`);
  }

  options(): Observable<BillingReviewOptions> {
    return this.http.get<BillingReviewOptions>(this.url('options'));
  }

  queue(filters: QueueFilters): Observable<QueueResponse> {
    return this.http.get<QueueResponse>(this.url('queue'), {
      params: params(filters).set('page', filters.page),
    });
  }

  /** CSV of the filtered queue (same filters and tab, no page). */
  export(filters: QueueFilters): Observable<ExportFile> {
    return this.http
      .get(this.url('export'), {
        params: params(filters),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(map((response) => toFile(response, `completed-jobs-review-${localIsoDate()}.csv`)));
  }

  detail(workOrderId: string): Observable<BillingReviewDetail> {
    return this.http.get<BillingReviewDetail>(this.orderUrl(workOrderId));
  }

  /** Image URL for `img src`; the session cookie authorizes it (BR-13). */
  evidenceUrl(workOrderId: string, evidenceId: string): string {
    return this.orderUrl(workOrderId, `/evidence/${encodeURIComponent(evidenceId)}`);
  }

  updateReview(workOrderId: string, body: ReviewUpdate): Observable<ReviewState> {
    return this.http.patch<ReviewState>(this.orderUrl(workOrderId, '/review'), body);
  }

  /** `201` created or `200` already existing; `changed` tells which. */
  generateInvoice(workOrderId: string, body: GenerateInvoiceBody): Observable<GeneratedInvoice> {
    return this.http.post<GeneratedInvoice>(this.orderUrl(workOrderId, '/invoice'), body);
  }
}

function params(filters: QueueFilters): HttpParams {
  let result = new HttpParams()
    .set('completed', filters.completed)
    .set('variance', filters.variance)
    .set('tab', filters.tab);
  if (filters.branchId !== null) {
    result = result.set('branchId', filters.branchId);
  }
  if (filters.technicianId !== null) {
    result = result.set('technicianId', filters.technicianId);
  }
  const search = filters.search.trim();
  return search === '' ? result : result.set('search', search);
}

function localIsoDate(): string {
  const now = new Date();
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

function toFile(response: HttpResponse<Blob>, fallbackName: string): ExportFile {
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const serverName = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1]?.trim();
  const safe = serverName !== undefined && /^[\w.-]+$/.test(serverName);
  return { blob: response.body ?? new Blob([]), fileName: safe ? serverName : fallbackName };
}
