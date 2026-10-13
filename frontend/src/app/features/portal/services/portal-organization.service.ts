import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG } from '../../../core/config/api.config';
import { portalUrl } from './portal-url';

/** The session organization's logo (`GET /portal/organization/logo`). */
@Injectable({ providedIn: 'root' })
export class PortalOrganizationService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  logo(): Observable<Blob> {
    return this.http.get(portalUrl(this.config, 'organization/logo'), { responseType: 'blob' });
  }
}
