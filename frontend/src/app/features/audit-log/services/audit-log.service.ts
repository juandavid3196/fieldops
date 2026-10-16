import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  AuditDetail,
  AuditFile,
  AuditFilterOptions,
  AuditPage,
  AuditQuery,
  AuditSummary,
} from '../models/audit-log.model';

/** `/audit-log` endpoints (Final contract: specs/audit-trail/spec.md). Scope is server-side only. */
@Injectable({ providedIn: 'root' })
export class AuditLogService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path = ''): string {
    return buildApiUrl(this.config, `audit-log${path}`);
  }

  summary(): Observable<AuditSummary> {
    return this.http.get<AuditSummary>(this.url('/summary'));
  }

  filters(): Observable<AuditFilterOptions> {
    return this.http.get<AuditFilterOptions>(this.url('/filters'));
  }

  list(query: AuditQuery, page: number, pageSize: number): Observable<AuditPage> {
    return this.http.get<AuditPage>(this.url(), {
      params: params(query).set('page', page).set('pageSize', pageSize),
    });
  }

  detail(id: number): Observable<AuditDetail> {
    return this.http.get<AuditDetail>(this.url(`/${id}`));
  }

  /** CSV of the filtered, sorted events (same query, no paging). */
  export(query: AuditQuery, fallbackDate: string): Observable<AuditFile> {
    return this.http
      .get(this.url('/export'), {
        params: params(query),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(map((response) => toFile(response, `audit-log-${fallbackDate}.csv`)));
  }
}

function params(query: AuditQuery): HttpParams {
  let result = new HttpParams().set('sort', query.sort).set('direction', query.direction);
  for (const [key, value] of [
    ['from', query.from],
    ['to', query.to],
    ['q', query.q.trim()],
    ['actorId', query.actor],
    ['action', query.action],
    ['entityType', query.entity],
    ['entityId', query.entityId],
    ['branchId', query.branch],
    ['impact', query.impact],
  ] as const) {
    if (value !== null && value !== '') {
      result = result.set(key, value);
    }
  }
  return result;
}

function toFile(response: HttpResponse<Blob>, fallbackName: string): AuditFile {
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const serverName = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1]?.trim();
  const safe = serverName !== undefined && /^[\w.-]+$/.test(serverName);
  return { blob: response.body ?? new Blob([]), fileName: safe ? serverName : fallbackName };
}
