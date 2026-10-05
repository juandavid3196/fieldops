import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  AssessmentBody,
  BoardStatus,
  CreateRequestBody,
  CustomerOptionsResponse,
  PipelineResponse,
  RequestDetail,
  RequestFilters,
  RequestMetrics,
  RequestOptions,
  Urgency,
} from '../models/requests.model';

/** `/service-requests` endpoints. Stateless; organization and branch scope are server-side only. */
@Injectable({ providedIn: 'root' })
export class RequestsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path: string): string {
    return buildApiUrl(this.config, `service-requests${path}`);
  }

  private item(id: string, path = ''): string {
    return this.url(`/${encodeURIComponent(id)}${path}`);
  }

  /** The four columns, or only `page.status` from `page.offset` (load more). */
  pipeline(
    filters: RequestFilters,
    page?: { readonly status: BoardStatus; readonly offset: number },
  ): Observable<PipelineResponse> {
    let params = new HttpParams();
    if (filters.assignee !== null) {
      params = params.set('assigneeUserId', filters.assignee);
    }
    if (filters.categoryId !== null) {
      params = params.set('categoryId', filters.categoryId);
    }
    if (filters.urgency !== null) {
      params = params.set('urgency', filters.urgency);
    }
    if (filters.source !== null) {
      params = params.set('source', filters.source);
    }
    if (filters.created !== null) {
      params = params.set('created', filters.created);
    }
    const search = filters.search.trim();
    if (search.length > 0) {
      params = params.set('search', search);
    }
    if (page !== undefined) {
      params = params.set('status', page.status).set('offset', page.offset);
    }
    return this.http.get<PipelineResponse>(this.url('/pipeline'), { params });
  }

  metrics(): Observable<RequestMetrics> {
    return this.http.get<RequestMetrics>(this.url('/metrics'));
  }

  options(): Observable<RequestOptions> {
    return this.http.get<RequestOptions>(this.url('/options'));
  }

  customerOptions(search: string): Observable<CustomerOptionsResponse> {
    const term = search.trim();
    const params = term.length > 0 ? new HttpParams().set('search', term) : new HttpParams();
    return this.http.get<CustomerOptionsResponse>(this.url('/customer-options'), { params });
  }

  detail(id: string): Observable<RequestDetail> {
    return this.http.get<RequestDetail>(this.item(id));
  }

  create(body: CreateRequestBody): Observable<RequestDetail> {
    return this.http.post<RequestDetail>(this.url(''), body);
  }

  startReview(id: string): Observable<RequestDetail> {
    return this.http.post<RequestDetail>(this.item(id, '/start-review'), null);
  }

  assign(id: string, assigneeUserId: string | null): Observable<RequestDetail> {
    return this.http.put<RequestDetail>(this.item(id, '/assignee'), { assigneeUserId });
  }

  changePriority(id: string, urgency: Urgency): Observable<RequestDetail> {
    return this.http.put<RequestDetail>(this.item(id, '/priority'), { urgency });
  }

  setBranch(id: string, branchId: string): Observable<RequestDetail> {
    return this.http.put<RequestDetail>(this.item(id, '/branch'), { branchId });
  }

  addNote(id: string, body: string): Observable<RequestDetail> {
    return this.http.post<RequestDetail>(this.item(id, '/notes'), { body });
  }

  requestInformation(id: string, body: string): Observable<RequestDetail> {
    return this.http.post<RequestDetail>(this.item(id, '/information-requests'), { body });
  }

  logResponse(id: string, body: string): Observable<RequestDetail> {
    return this.http.post<RequestDetail>(this.item(id, '/customer-responses'), { body });
  }

  scheduleAssessment(id: string, body: AssessmentBody): Observable<RequestDetail> {
    return this.http.post<RequestDetail>(this.item(id, '/assessment'), body);
  }

  rescheduleAssessment(id: string, body: AssessmentBody): Observable<RequestDetail> {
    return this.http.put<RequestDetail>(this.item(id, '/assessment'), body);
  }

  cancelAssessment(id: string, notifyCustomer: boolean): Observable<RequestDetail> {
    return this.http.post<RequestDetail>(this.item(id, '/assessment/cancel'), { notifyCustomer });
  }

  completeAssessment(id: string): Observable<RequestDetail> {
    return this.http.post<RequestDetail>(this.item(id, '/assessment/complete'), null);
  }

  markReadyForQuote(id: string): Observable<RequestDetail> {
    return this.http.post<RequestDetail>(this.item(id, '/ready-for-quote'), null);
  }

  moveToReview(id: string): Observable<RequestDetail> {
    return this.http.post<RequestDetail>(this.item(id, '/move-to-review'), null);
  }

  cancel(id: string, reason: string): Observable<RequestDetail> {
    return this.http.post<RequestDetail>(this.item(id, '/cancel'), { reason });
  }

  /** Authenticated attachment content (BR-16); callers own the object URL they create from it. */
  downloadAttachment(id: string, attachmentId: string): Observable<Blob> {
    return this.http.get(this.item(id, `/attachments/${encodeURIComponent(attachmentId)}`), {
      responseType: 'blob',
    });
  }

  uploadAttachments(id: string, files: readonly File[]): Observable<RequestDetail> {
    const form = new FormData();
    for (const file of files) {
      form.append('attachments', file, file.name);
    }
    return this.http.post<RequestDetail>(this.item(id, '/attachments'), form);
  }
}
