import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG } from '../../../core/config/api.config';
import { ServiceRequestForm } from '../../service-request/models/service-request.model';
import { Page, PortalRequestCreated, RequestDetail, RequestRow } from '../models/portal.model';
import { encode, listParams, portalUrl } from './portal-url';

/** Requests module and the new-request submission (BR-27, BR-28). */
@Injectable({ providedIn: 'root' })
export class PortalRequestsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  list(page: number, propertyId: string | null): Observable<Page<RequestRow>> {
    return this.http.get<Page<RequestRow>>(portalUrl(this.config, 'requests'), {
      params: listParams(page, propertyId),
    });
  }

  get(requestId: string): Observable<RequestDetail> {
    return this.http.get<RequestDetail>(portalUrl(this.config, `requests/${encode(requestId)}`));
  }

  /** The public form configuration of the session organization. */
  form(): Observable<ServiceRequestForm> {
    return this.http.get<ServiceRequestForm>(portalUrl(this.config, 'service-request-form'));
  }

  /** Multipart with the public part names: JSON part `request` plus `attachments` files. */
  create(payload: object, files: readonly File[]): Observable<PortalRequestCreated> {
    const body = new FormData();
    body.append('request', new Blob([JSON.stringify(payload)], { type: 'application/json' }));
    for (const file of files) {
      body.append('attachments', file, file.name);
    }
    return this.http.post<PortalRequestCreated>(portalUrl(this.config, 'service-requests'), body);
  }
}
