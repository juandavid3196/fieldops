import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import { BusinessHoursBody, BusinessHoursRequest } from '../models/business-hours.model';

/** `/branches/{id}/business-hours` endpoints (business-hours FR-03, FR-04). */
@Injectable({ providedIn: 'root' })
export class BusinessHoursService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  get(branchId: string): Observable<BusinessHoursBody> {
    return this.http.get<BusinessHoursBody>(
      buildApiUrl(this.config, `branches/${branchId}/business-hours`),
    );
  }

  update(branchId: string, request: BusinessHoursRequest): Observable<BusinessHoursBody> {
    return this.http.put<BusinessHoursBody>(
      buildApiUrl(this.config, `branches/${branchId}/business-hours`),
      request,
    );
  }
}
