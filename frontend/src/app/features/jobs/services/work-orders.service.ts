import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  ChecklistTemplate,
  ChecklistTemplateBody,
  WorkOrderBody,
  WorkOrderCreated,
  WorkOrderDetail,
  WorkOrderEditor,
  WorkOrderList,
} from '../models/work-order.model';

export const JOBS_PAGE_SIZE = 25;

/** Work order endpoints (Final contract: specs/create-work-order/spec.md). Organization scope is server-side only. */
@Injectable({ providedIn: 'root' })
export class WorkOrdersService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private editorUrl(quoteId: string, path = ''): string {
    return buildApiUrl(this.config, `quotes/${encodeURIComponent(quoteId)}/work-order${path}`);
  }

  editor(quoteId: string): Observable<WorkOrderEditor> {
    return this.http.get<WorkOrderEditor>(this.editorUrl(quoteId));
  }

  /** `201` for an inserted draft, `200` for an updated one. */
  saveDraft(
    quoteId: string,
    body: WorkOrderBody,
    updatedAt: string | null,
  ): Observable<HttpResponse<WorkOrderEditor>> {
    return this.http.put<WorkOrderEditor>(
      this.editorUrl(quoteId, '/draft'),
      { ...body, updatedAt },
      { observe: 'response' },
    );
  }

  /** `201` created, `200` already created; both carry the order id. */
  create(
    quoteId: string,
    body: WorkOrderBody,
    updatedAt: string | null,
  ): Observable<WorkOrderCreated> {
    return this.http.post<WorkOrderCreated>(this.editorUrl(quoteId), { ...body, updatedAt });
  }

  list(page: number): Observable<WorkOrderList> {
    const params = new HttpParams().set('page', page).set('pageSize', JOBS_PAGE_SIZE);
    return this.http.get<WorkOrderList>(buildApiUrl(this.config, 'work-orders'), { params });
  }

  detail(id: string): Observable<WorkOrderDetail> {
    return this.http.get<WorkOrderDetail>(
      buildApiUrl(this.config, `work-orders/${encodeURIComponent(id)}`),
    );
  }

  templates(): Observable<readonly ChecklistTemplate[]> {
    return this.http.get<readonly ChecklistTemplate[]>(
      buildApiUrl(this.config, 'checklist-templates'),
    );
  }

  createTemplate(body: ChecklistTemplateBody): Observable<ChecklistTemplate> {
    return this.http.post<ChecklistTemplate>(buildApiUrl(this.config, 'checklist-templates'), body);
  }
}
