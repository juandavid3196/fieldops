import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  CalendarQuery,
  CalendarResponse,
  DispatchBody,
  DispatchOptions,
  EvaluationRequest,
  UNSCHEDULED_PAGE_SIZE,
  UnscheduledQuery,
  UnscheduledResponse,
  VisitDispatchDetail,
  VisitDispatchResult,
  VisitEvaluation,
} from '../models/schedule.model';

/** `/dispatch` endpoints. Organization and branch scope are resolved server-side only. */
@Injectable({ providedIn: 'root' })
export class ScheduleService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path: string): string {
    return buildApiUrl(this.config, `dispatch/${path}`);
  }

  options(): Observable<DispatchOptions> {
    return this.http.get<DispatchOptions>(this.url('options'));
  }

  calendar(query: CalendarQuery): Observable<CalendarResponse> {
    let params = new HttpParams()
      .set('branchId', query.branchId)
      .set('view', query.view)
      .set('date', query.date)
      .set('status', query.status);
    for (const id of query.technicianIds) {
      params = params.append('technicianIds', id);
    }
    if (query.skillId !== null) {
      params = params.set('skillId', query.skillId);
    }
    return this.http.get<CalendarResponse>(this.url('calendar'), { params });
  }

  unscheduled(query: UnscheduledQuery): Observable<UnscheduledResponse> {
    let params = new HttpParams()
      .set('branchId', query.branchId)
      .set('filter', query.filter)
      .set('page', query.page)
      .set('pageSize', UNSCHEDULED_PAGE_SIZE);
    const search = query.search.trim();
    if (search.length > 0) {
      params = params.set('search', search);
    }
    if (query.skillId !== null) {
      params = params.set('skillId', query.skillId);
    }
    return this.http.get<UnscheduledResponse>(this.url('unscheduled'), { params });
  }

  visit(visitId: string): Observable<VisitDispatchDetail> {
    return this.http.get<VisitDispatchDetail>(this.url(`visits/${encodeURIComponent(visitId)}`));
  }

  evaluate(visitId: string, body: EvaluationRequest): Observable<VisitEvaluation> {
    return this.http.post<VisitEvaluation>(
      this.url(`visits/${encodeURIComponent(visitId)}/evaluation`),
      body,
    );
  }

  dispatch(visitId: string, body: DispatchBody): Observable<VisitDispatchResult> {
    return this.http.put<VisitDispatchResult>(
      this.url(`visits/${encodeURIComponent(visitId)}`),
      body,
    );
  }
}
