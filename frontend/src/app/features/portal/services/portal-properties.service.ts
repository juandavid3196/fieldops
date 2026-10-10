import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG } from '../../../core/config/api.config';
import { CreatePropertyBody, PortalProperty, UpdatePropertyBody } from '../models/portal.model';
import { encode, portalUrl } from './portal-url';

/** Properties of the session customer (BR-33). */
@Injectable({ providedIn: 'root' })
export class PortalPropertiesService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  list(): Observable<readonly PortalProperty[]> {
    return this.http.get<readonly PortalProperty[]>(portalUrl(this.config, 'properties'));
  }

  create(body: CreatePropertyBody): Observable<PortalProperty> {
    return this.http.post<PortalProperty>(portalUrl(this.config, 'properties'), body);
  }

  update(propertyId: string, body: UpdatePropertyBody): Observable<PortalProperty> {
    return this.http.patch<PortalProperty>(
      portalUrl(this.config, `properties/${encode(propertyId)}`),
      body,
    );
  }
}
