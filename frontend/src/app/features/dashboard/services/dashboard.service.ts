import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  DashboardOptions,
  DashboardQuery,
  DashboardResponse,
  PerformanceQuery,
  PerformanceResponse,
} from '../models/dashboard.model';

/** `/operations-dashboard` endpoints. Organization and branch scope are resolved server-side only. */
@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path: string): string {
    return buildApiUrl(this.config, `operations-dashboard${path}`);
  }

  private params(query: DashboardQuery): HttpParams {
    let params = new HttpParams().set('date', query.date);
    if (query.branchId !== null) {
      params = params.set('branchId', query.branchId);
    }
    if (query.technicianId !== null) {
      params = params.set('technicianId', query.technicianId);
    }
    if (query.categoryId !== null) {
      params = params.set('categoryId', query.categoryId);
    }
    return params;
  }

  options(): Observable<DashboardOptions> {
    return this.http.get<DashboardOptions>(this.url('/options'));
  }

  dashboard(query: DashboardQuery): Observable<DashboardResponse> {
    return this.http.get<DashboardResponse>(this.url(''), { params: this.params(query) });
  }

  performance(query: PerformanceQuery): Observable<PerformanceResponse> {
    return this.http.get<PerformanceResponse>(this.url('/performance'), {
      params: this.params(query).set('period', query.period),
    });
  }
}
