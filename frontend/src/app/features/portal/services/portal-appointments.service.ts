import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG } from '../../../core/config/api.config';
import {
  AppointmentScope,
  Page,
  PortalAppointment,
  RescheduleRequestBody,
} from '../models/portal.model';
import { encode, listParams, portalUrl } from './portal-url';

/** Appointments module, reschedule requests and work-order reports (BR-29, BR-31, BR-32). */
@Injectable({ providedIn: 'root' })
export class PortalAppointmentsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  list(
    scope: AppointmentScope,
    page: number,
    propertyId: string | null,
  ): Observable<Page<PortalAppointment>> {
    return this.http.get<Page<PortalAppointment>>(portalUrl(this.config, 'appointments'), {
      params: listParams(page, propertyId, { scope }),
    });
  }

  get(visitId: string): Observable<PortalAppointment> {
    return this.http.get<PortalAppointment>(
      portalUrl(this.config, `appointments/${encode(visitId)}`),
    );
  }

  requestReschedule(
    visitId: string,
    body: RescheduleRequestBody,
  ): Observable<{ readonly requestedOn: string }> {
    return this.http.post<{ readonly requestedOn: string }>(
      portalUrl(this.config, `appointments/${encode(visitId)}/reschedule-requests`),
      body,
    );
  }

  /** Completion report PDF of a work order (BR-31). */
  workOrderReport(workOrderId: string): Observable<Blob> {
    return this.http.get(
      portalUrl(this.config, `work-orders/${encode(workOrderId)}/completion-report`),
      { responseType: 'blob' },
    );
  }
}
