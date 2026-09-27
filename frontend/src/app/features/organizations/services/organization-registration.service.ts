import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  OrganizationRegistrationRequest,
  OrganizationRegistrationResponse,
} from '../models/organization-registration.model';

/** Public, pre-tenant organization registration (FR-01 to FR-16). No session is created. */
@Injectable({ providedIn: 'root' })
export class OrganizationRegistrationService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  /** Posts `POST /organization-registrations` with the already-built BR-01 request body. */
  register(request: OrganizationRegistrationRequest): Observable<OrganizationRegistrationResponse> {
    return this.http.post<OrganizationRegistrationResponse>(
      buildApiUrl(this.config, 'organization-registrations'),
      request,
    );
  }
}
