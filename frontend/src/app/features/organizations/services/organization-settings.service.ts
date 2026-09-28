import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  OrganizationSettingsRequest,
  OrganizationSettingsResponse,
} from '../models/company-settings.model';

/** `GET`/`PUT /organization-settings` (FR-03, FR-04). */
@Injectable({ providedIn: 'root' })
export class OrganizationSettingsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  get(): Observable<OrganizationSettingsResponse> {
    return this.http.get<OrganizationSettingsResponse>(
      buildApiUrl(this.config, 'organization-settings'),
    );
  }

  update(request: OrganizationSettingsRequest): Observable<OrganizationSettingsResponse> {
    return this.http.put<OrganizationSettingsResponse>(
      buildApiUrl(this.config, 'organization-settings'),
      request,
    );
  }
}
