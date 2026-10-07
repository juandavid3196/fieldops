import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  TechnicianVisitDetail,
  TodayResponse,
  TodayTechnician,
  TravelResult,
} from '../models/technician-visits.model';

/** `/technician` endpoints. The technician is resolved server-side from the session. */
@Injectable({ providedIn: 'root' })
export class TechnicianVisitsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly currentTechnician = signal<TodayTechnician | null>(null);

  /** Last technician identity returned by Today; the shell avatar reads it. */
  readonly technician = this.currentTechnician.asReadonly();

  today(): Observable<TodayResponse> {
    return this.http
      .get<TodayResponse>(buildApiUrl(this.config, 'technician/today'))
      .pipe(tap((response) => this.currentTechnician.set(response.technician)));
  }

  visit(visitId: string): Observable<TechnicianVisitDetail> {
    return this.http.get<TechnicianVisitDetail>(
      buildApiUrl(this.config, `technician/visits/${encodeURIComponent(visitId)}`),
    );
  }

  /** Primary technician only; the backend decides (BR-02). No request body. */
  startTravel(visitId: string): Observable<TravelResult> {
    return this.http.post<TravelResult>(this.visitUrl(visitId, 'start-travel'), null);
  }

  arrive(visitId: string): Observable<TravelResult> {
    return this.http.post<TravelResult>(this.visitUrl(visitId, 'arrive'), null);
  }

  /** Image URL for `img src`; the session cookie authorizes it (BR-06). */
  assessmentPhotoUrl(visitId: string, photoId: string): string {
    return this.visitUrl(visitId, `assessment-photos/${encodeURIComponent(photoId)}`);
  }

  private visitUrl(visitId: string, suffix: string): string {
    return buildApiUrl(this.config, `technician/visits/${encodeURIComponent(visitId)}/${suffix}`);
  }
}
