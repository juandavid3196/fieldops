import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  CatalogSkill,
  CatalogSkillRequest,
  ExceptionRequest,
  SkillsAvailability,
  SkillsAvailabilityRequest,
  TechnicianException,
} from '../models/skills-availability.model';

/** Skills & availability, exception and skill-catalog endpoints. Scope is resolved server-side. */
@Injectable({ providedIn: 'root' })
export class SkillsAvailabilityService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path: string): string {
    return buildApiUrl(this.config, path);
  }

  private page(id: string): string {
    return `team/technicians/${encodeURIComponent(id)}/skills-availability`;
  }

  private exceptions(id: string, exceptionId?: string, action?: string): string {
    const base = `team/technicians/${encodeURIComponent(id)}/exceptions`;
    const withId = exceptionId === undefined ? base : `${base}/${encodeURIComponent(exceptionId)}`;
    return action === undefined ? withId : `${withId}/${action}`;
  }

  get(id: string): Observable<SkillsAvailability> {
    return this.http.get<SkillsAvailability>(this.url(this.page(id)));
  }

  save(id: string, body: SkillsAvailabilityRequest): Observable<SkillsAvailability> {
    return this.http.put<SkillsAvailability>(this.url(this.page(id)), body);
  }

  createException(id: string, body: ExceptionRequest): Observable<TechnicianException> {
    return this.http.post<TechnicianException>(this.url(this.exceptions(id)), body);
  }

  updateException(
    id: string,
    exceptionId: string,
    body: ExceptionRequest & { readonly version: string },
  ): Observable<TechnicianException> {
    return this.http.put<TechnicianException>(this.url(this.exceptions(id, exceptionId)), body);
  }

  cancelException(
    id: string,
    exceptionId: string,
    version: string,
  ): Observable<TechnicianException> {
    return this.http.post<TechnicianException>(
      this.url(this.exceptions(id, exceptionId, 'cancel')),
      { version },
    );
  }

  activateException(
    id: string,
    exceptionId: string,
    version: string,
  ): Observable<TechnicianException> {
    return this.http.post<TechnicianException>(
      this.url(this.exceptions(id, exceptionId, 'activate')),
      { version },
    );
  }

  catalog(): Observable<CatalogSkill[]> {
    return this.http.get<CatalogSkill[]>(this.url('team/skills'));
  }

  createSkill(body: CatalogSkillRequest): Observable<CatalogSkill> {
    return this.http.post<CatalogSkill>(this.url('team/skills'), body);
  }

  updateSkill(skillId: string, body: CatalogSkillRequest): Observable<CatalogSkill> {
    return this.http.put<CatalogSkill>(
      this.url(`team/skills/${encodeURIComponent(skillId)}`),
      body,
    );
  }

  setSkillActive(skillId: string, active: boolean): Observable<void> {
    return this.http.post<void>(
      this.url(`team/skills/${encodeURIComponent(skillId)}/${active ? 'activate' : 'deactivate'}`),
      null,
    );
  }
}
