import { HttpClient, HttpEventType } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, filter, map, tap } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  CatalogItem,
  EvidenceType,
  EvidenceUploadEvent,
  NewMaterial,
  TaskUpdate,
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

  private readonly titleOverride = signal<string | null>(null);

  /** Last technician identity returned by Today; the shell avatar reads it. */
  readonly technician = this.currentTechnician.asReadonly();

  /** Top-bar title set by the job page while it shows Job in progress; `null` keeps the route title. */
  readonly pageTitle = this.titleOverride.asReadonly();

  setPageTitle(title: string | null): void {
    this.titleOverride.set(title);
  }

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

  startJob(visitId: string): Observable<TravelResult> {
    return this.http.post<TravelResult>(this.visitUrl(visitId, 'start-job'), null);
  }

  pause(visitId: string): Observable<TravelResult> {
    return this.http.post<TravelResult>(this.visitUrl(visitId, 'pause'), null);
  }

  resume(visitId: string): Observable<TravelResult> {
    return this.http.post<TravelResult>(this.visitUrl(visitId, 'resume'), null);
  }

  updateTask(
    visitId: string,
    taskId: string,
    update: TaskUpdate,
  ): Observable<TechnicianVisitDetail> {
    return this.http.patch<TechnicianVisitDetail>(
      this.visitUrl(visitId, `tasks/${encodeURIComponent(taskId)}`),
      update,
    );
  }

  addTask(visitId: string, label: string): Observable<TechnicianVisitDetail> {
    return this.http.post<TechnicianVisitDetail>(this.visitUrl(visitId, 'tasks'), { label });
  }

  setPlannedUsed(
    visitId: string,
    plannedMaterialId: string,
    usedQuantity: number,
  ): Observable<TechnicianVisitDetail> {
    return this.http.put<TechnicianVisitDetail>(
      this.visitUrl(visitId, `planned-materials/${encodeURIComponent(plannedMaterialId)}`),
      { usedQuantity },
    );
  }

  addMaterial(visitId: string, material: NewMaterial): Observable<TechnicianVisitDetail> {
    return this.http.post<TechnicianVisitDetail>(this.visitUrl(visitId, 'materials'), material);
  }

  /** Additional materials only; `0` deletes the row. */
  setMaterialQuantity(
    visitId: string,
    materialId: string,
    quantity: number,
  ): Observable<TechnicianVisitDetail> {
    return this.http.put<TechnicianVisitDetail>(
      this.visitUrl(visitId, `materials/${encodeURIComponent(materialId)}`),
      { quantity },
    );
  }

  searchCatalog(visitId: string, search: string): Observable<CatalogItem[]> {
    return this.http.get<CatalogItem[]>(this.visitUrl(visitId, 'material-catalog'), {
      params: { search },
    });
  }

  /** Multipart upload; progress events carry the sent percentage, the response carries the visit. */
  uploadEvidence(visitId: string, file: File, type: EvidenceType): Observable<EvidenceUploadEvent> {
    const body = new FormData();
    body.append('file', file);
    body.append('type', type);
    return this.http
      .post<TechnicianVisitDetail>(this.visitUrl(visitId, 'evidence'), body, {
        reportProgress: true,
        observe: 'events',
      })
      .pipe(
        map((event): EvidenceUploadEvent | null => {
          if (event.type === HttpEventType.UploadProgress) {
            const percent = event.total ? Math.round((event.loaded / event.total) * 100) : 0;
            return { kind: 'progress', percent: Math.min(100, percent) };
          }
          return event.type === HttpEventType.Response && event.body !== null
            ? { kind: 'done', visit: event.body }
            : null;
        }),
        filter((event): event is EvidenceUploadEvent => event !== null),
      );
  }

  deleteEvidence(visitId: string, evidenceId: string): Observable<TechnicianVisitDetail> {
    return this.http.delete<TechnicianVisitDetail>(
      this.visitUrl(visitId, `evidence/${encodeURIComponent(evidenceId)}`),
    );
  }

  saveNotes(visitId: string, notes: string): Observable<TechnicianVisitDetail> {
    return this.http.put<TechnicianVisitDetail>(this.visitUrl(visitId, 'notes'), { notes });
  }

  /** Image URL for the job photos (`img src`); the session cookie authorizes it (BR-12). */
  evidencePhotoUrl(visitId: string, evidenceId: string): string {
    return this.visitUrl(visitId, `evidence/${encodeURIComponent(evidenceId)}`);
  }

  /** Image URL for `img src`; the session cookie authorizes it (BR-06). */
  assessmentPhotoUrl(visitId: string, photoId: string): string {
    return this.visitUrl(visitId, `assessment-photos/${encodeURIComponent(photoId)}`);
  }

  private visitUrl(visitId: string, suffix: string): string {
    return buildApiUrl(this.config, `technician/visits/${encodeURIComponent(visitId)}/${suffix}`);
  }
}
