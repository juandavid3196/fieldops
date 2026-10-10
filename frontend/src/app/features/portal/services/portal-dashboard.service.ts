import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG } from '../../../core/config/api.config';
import { ActivityRow, Dashboard, Page } from '../models/portal.model';
import { listParams, portalUrl } from './portal-url';

/** Home dashboard, recent activity and the message to the organization (BR-18 … BR-26, BR-35). */
@Injectable({ providedIn: 'root' })
export class PortalDashboardService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  /** `propertyId` absent means All properties (BR-18). */
  dashboard(propertyId: string | null): Observable<Dashboard> {
    const params =
      propertyId === null ? new HttpParams() : new HttpParams().set('propertyId', propertyId);
    return this.http.get<Dashboard>(portalUrl(this.config, 'dashboard'), { params });
  }

  activity(page: number, propertyId: string | null): Observable<Page<ActivityRow>> {
    return this.http.get<Page<ActivityRow>>(portalUrl(this.config, 'activity'), {
      params: listParams(page, propertyId),
    });
  }

  sendMessage(message: string): Observable<void> {
    return this.http.post<void>(portalUrl(this.config, 'messages'), { message });
  }
}
