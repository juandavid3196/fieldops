import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  ReportData,
  ReportExportItem,
  ReportFile,
  ReportFormat,
  ReportOptions,
  ReportsRoute,
} from '../models/reports.model';
import { exportQueryParams, reportQueryParams } from '../utils/report-params';

/** `/reports` endpoints (Final contract: specs/reports-workspace/spec.md). Scope is server-side only. */
@Injectable({ providedIn: 'root' })
export class ReportsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path: string): string {
    return buildApiUrl(this.config, `reports${path}`);
  }

  options(): Observable<ReportOptions> {
    return this.http.get<ReportOptions>(this.url('/options'));
  }

  report(route: ReportsRoute): Observable<ReportData> {
    return this.http.get<ReportData>(this.url(`/${route.report}`), {
      params: new HttpParams({ fromObject: reportQueryParams(route) }),
    });
  }

  /** BR-14/BR-15: the applied state as CSV or PDF, named by the server. */
  export(route: ReportsRoute, format: ReportFormat, today: string): Observable<ReportFile> {
    return this.http
      .get(this.url(`/${route.report}/export`), {
        params: new HttpParams({ fromObject: exportQueryParams(route, format) }),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(map((response) => toFile(response, fallbackName(route, format, today))));
  }

  /** BR-17: the caller's own recent report exports. */
  exports(): Observable<readonly ReportExportItem[]> {
    return this.http
      .get<{ items: readonly ReportExportItem[] }>(this.url('/exports'))
      .pipe(map((response) => response.items));
  }
}

/** BR-14 name, used only when the server does not expose `Content-Disposition`. */
function fallbackName(route: ReportsRoute, format: ReportFormat, today: string): string {
  const key = route.report.replaceAll('_', '-');
  return route.report === 'invoice_aging'
    ? `${key}-${today}.${format}`
    : `${key}-${route.from}-to-${route.to}.${format}`;
}

function toFile(response: HttpResponse<Blob>, fallback: string): ReportFile {
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const serverName = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1]?.trim();
  const safe = serverName !== undefined && /^[\w.-]+$/.test(serverName);
  return { blob: response.body ?? new Blob([]), fileName: safe ? serverName : fallback };
}
