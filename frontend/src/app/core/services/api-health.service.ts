import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../config/api.config';
import { HealthCheckResponse } from '../models/health-check.model';

/** Verifies connectivity with the FieldOps API through its health endpoint. */
@Injectable({ providedIn: 'root' })
export class ApiHealthService {
  private readonly http = inject(HttpClient);
  private readonly apiConfig = inject(API_CONFIG);

  check(): Observable<HealthCheckResponse> {
    return this.http.get<HealthCheckResponse>(buildApiUrl(this.apiConfig, 'health'));
  }
}
