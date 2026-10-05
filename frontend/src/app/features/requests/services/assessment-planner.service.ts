import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  AssessmentCalendar,
  AssessmentPlanner,
  CalendarQuery,
  PlannerQuery,
} from '../models/requests.model';

/** Read-only planning endpoints of the Schedule assessment page; organization and scope are server-side. */
@Injectable({ providedIn: 'root' })
export class AssessmentPlannerService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(requestId: string, path: string): string {
    return buildApiUrl(
      this.config,
      `service-requests/${encodeURIComponent(requestId)}/assessment/${path}`,
    );
  }

  getPlanner(requestId: string, query: PlannerQuery): Observable<AssessmentPlanner> {
    let params = new HttpParams()
      .set('date', query.date)
      .set('start', query.start)
      .set('durationMinutes', query.durationMinutes);
    if (query.branchId !== undefined) {
      params = params.set('branchId', query.branchId);
    }
    return this.http.get<AssessmentPlanner>(this.url(requestId, 'planner'), { params });
  }

  getCalendar(requestId: string, query: CalendarQuery): Observable<AssessmentCalendar> {
    const params = new HttpParams()
      .set('technicianId', query.technicianId)
      .set('from', query.from)
      .set('to', query.to);
    return this.http.get<AssessmentCalendar>(this.url(requestId, 'calendar'), { params });
  }
}
