import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  LinkableAccount,
  SkillCoverage,
  TeamMetrics,
  TeamOptions,
  TeamPeriod,
  TechnicianDetail,
  TechnicianListQuery,
  TechnicianListResponse,
  TechnicianRequest,
} from '../models/team.model';
import {
  AvailabilityDay,
  AvailabilityPage,
  AvailabilityQuery,
  SkillAssignments,
  SkillsMatrixPage,
  SkillsMatrixQuery,
  TechnicianAvailability,
  TimeOff,
  TimeOffRequest,
} from '../models/team-planning.model';

/** `/team` endpoints. Organization and branch scope are resolved server-side only. */
@Injectable({ providedIn: 'root' })
export class TeamService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path: string): string {
    return buildApiUrl(this.config, path);
  }

  options(): Observable<TeamOptions> {
    return this.http.get<TeamOptions>(this.url('team/options'));
  }

  metrics(branchId: string | null, period: TeamPeriod): Observable<TeamMetrics> {
    let params = new HttpParams().set('period', period);
    if (branchId !== null) {
      params = params.set('branchId', branchId);
    }
    return this.http.get<TeamMetrics>(this.url('team/metrics'), { params });
  }

  list(query: TechnicianListQuery): Observable<TechnicianListResponse> {
    let params = new HttpParams()
      .set('status', query.status)
      .set('accountLink', query.accountLink)
      .set('sort', query.sort)
      .set('period', query.period);
    const search = query.search.trim();
    if (search.length > 0) {
      params = params.set('search', search);
    }
    if (query.branchId !== null) {
      params = params.set('branchId', query.branchId);
    }
    if (query.skillId !== null) {
      params = params.set('skillId', query.skillId);
    }
    params = params.set('page', query.page);
    return this.http.get<TechnicianListResponse>(this.url('team/technicians'), { params });
  }

  get(id: string, period: TeamPeriod = 'today'): Observable<TechnicianDetail> {
    return this.http.get<TechnicianDetail>(this.url(`team/technicians/${id}`), {
      params: new HttpParams().set('period', period),
    });
  }

  me(): Observable<TechnicianDetail> {
    return this.http.get<TechnicianDetail>(this.url('team/me'));
  }

  create(body: TechnicianRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(this.url('team/technicians'), body);
  }

  update(id: string, body: TechnicianRequest): Observable<TechnicianDetail> {
    return this.http.put<TechnicianDetail>(this.url(`team/technicians/${id}`), body);
  }

  activate(id: string): Observable<void> {
    return this.http.post<void>(this.url(`team/technicians/${id}/activate`), null);
  }

  deactivate(id: string): Observable<void> {
    return this.http.post<void>(this.url(`team/technicians/${id}/deactivate`), null);
  }

  linkableAccounts(search: string): Observable<LinkableAccount[]> {
    let params = new HttpParams();
    if (search.trim().length > 0) {
      params = params.set('search', search.trim());
    }
    return this.http.get<LinkableAccount[]>(this.url('team/linkable-accounts'), { params });
  }

  link(id: string, organizationUserId: string): Observable<void> {
    return this.http.put<void>(this.url(`team/technicians/${id}/account-link`), {
      organizationUserId,
    });
  }

  unlink(id: string): Observable<void> {
    return this.http.delete<void>(this.url(`team/technicians/${id}/account-link`));
  }

  skillCoverage(branchId: string | null): Observable<SkillCoverage[]> {
    let params = new HttpParams();
    if (branchId !== null) {
      params = params.set('branchId', branchId);
    }
    return this.http.get<SkillCoverage[]>(this.url('team/skill-coverage'), { params });
  }

  // Availability and skills tabs (team-availability-skills)

  availability(query: AvailabilityQuery): Observable<AvailabilityPage> {
    let params = new HttpParams().set('availability', query.availability).set('page', query.page);
    if (query.weekStart !== null) {
      params = params.set('weekStart', query.weekStart);
    }
    const search = query.search.trim();
    if (search.length > 0) {
      params = params.set('search', search);
    }
    if (query.branchId !== null) {
      params = params.set('branchId', query.branchId);
    }
    return this.http.get<AvailabilityPage>(this.url('team/availability'), { params });
  }

  technicianAvailability(id: string): Observable<TechnicianAvailability> {
    return this.http.get<TechnicianAvailability>(this.url(`team/technicians/${id}/availability`));
  }

  saveAvailability(
    id: string,
    body: { readonly updatedAt: string; readonly days: readonly AvailabilityDay[] },
  ): Observable<TechnicianAvailability> {
    return this.http.put<TechnicianAvailability>(
      this.url(`team/technicians/${id}/availability`),
      body,
    );
  }

  useBranchHours(id: string, updatedAt: string): Observable<TechnicianAvailability> {
    return this.http.post<TechnicianAvailability>(
      this.url(`team/technicians/${id}/availability/use-branch-hours`),
      { updatedAt },
    );
  }

  addTimeOff(id: string, body: TimeOffRequest): Observable<TimeOff> {
    return this.http.post<TimeOff>(this.url(`team/technicians/${id}/time-off`), body);
  }

  removeTimeOff(
    id: string,
    timeOffId: string,
    updatedAt: string,
  ): Observable<{ readonly timeOff: TimeOff | null }> {
    return this.http.post<{ readonly timeOff: TimeOff | null }>(
      this.url(`team/technicians/${id}/time-off/${timeOffId}/retire`),
      { updatedAt },
    );
  }

  skillsMatrix(query: SkillsMatrixQuery): Observable<SkillsMatrixPage> {
    let params = new HttpParams()
      .set('coverage', query.coverage)
      .set('sort', query.sort)
      .set('page', query.page);
    const search = query.search.trim();
    if (search.length > 0) {
      params = params.set('search', search);
    }
    if (query.branchId !== null) {
      params = params.set('branchId', query.branchId);
    }
    if (query.skillId !== null) {
      params = params.set('skillId', query.skillId);
    }
    return this.http.get<SkillsMatrixPage>(this.url('team/skills-matrix'), { params });
  }

  skillAssignments(id: string): Observable<SkillAssignments> {
    return this.http.get<SkillAssignments>(this.url(`team/technicians/${id}/skill-assignments`));
  }

  saveSkillAssignments(
    id: string,
    body: { readonly updatedAt: string; readonly skillIds: readonly string[] },
  ): Observable<SkillAssignments> {
    return this.http.put<SkillAssignments>(
      this.url(`team/technicians/${id}/skill-assignments`),
      body,
    );
  }
}
