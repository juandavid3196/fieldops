import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import { LanguageService } from '../../../core/i18n/language.service';
import {
  OrganizationRegistrationRequest,
  OrganizationRegistrationResponse,
} from '../models/organization-registration.model';

/** Public, pre-tenant organization registration (FR-01 to FR-16). No session is created. */
@Injectable({ providedIn: 'root' })
export class OrganizationRegistrationService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly language = inject(LanguageService);

  /** Posts `POST /organization-registrations` with the already-built BR-01 request body. */
  register(request: OrganizationRegistrationRequest): Observable<OrganizationRegistrationResponse> {
    return this.http.post<OrganizationRegistrationResponse>(
      buildApiUrl(this.config, 'organization-registrations'),
      // The active UI language is stored with the new account (app-i18n-en-es BR-08).
      { ...request, preferredLanguage: this.language.lang() },
    );
  }
}
