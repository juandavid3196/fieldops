import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  ServiceRequestCreated,
  ServiceRequestForm,
  ServiceRequestPayload,
} from '../models/service-request.model';

/** Anonymous public service-request endpoints. */
@Injectable({ providedIn: 'root' })
export class ServiceRequestService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  getForm(slug: string): Observable<ServiceRequestForm> {
    return this.http.get<ServiceRequestForm>(this.url(slug, 'service-request-form'));
  }

  /** Multipart: JSON part `request` plus `attachments` files. The browser sets the boundary. */
  submit(
    slug: string,
    payload: ServiceRequestPayload,
    files: readonly File[],
  ): Observable<ServiceRequestCreated> {
    const body = new FormData();
    body.append('request', new Blob([JSON.stringify(payload)], { type: 'application/json' }));
    for (const file of files) {
      body.append('attachments', file, file.name);
    }
    return this.http.post<ServiceRequestCreated>(this.url(slug, 'service-requests'), body);
  }

  private url(slug: string, resource: string): string {
    return buildApiUrl(this.config, `public/organizations/${encodeURIComponent(slug)}/${resource}`);
  }
}
