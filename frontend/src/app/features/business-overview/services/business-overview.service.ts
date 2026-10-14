import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import { DownloadedCsv } from '../../../shared/utils/csv-file';
import {
  BusinessOptions,
  BusinessOverview,
  BusinessQuery,
} from '../models/business-overview.model';

/** `/business-overview` endpoints. The organization is resolved server-side only. */
@Injectable({ providedIn: 'root' })
export class BusinessOverviewService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path: string): string {
    return buildApiUrl(this.config, `business-overview${path}`);
  }

  private params(query: BusinessQuery): HttpParams {
    const params = new HttpParams().set('from', query.from).set('to', query.to);
    return query.branchId === null ? params : params.set('branchId', query.branchId);
  }

  options(): Observable<BusinessOptions> {
    return this.http.get<BusinessOptions>(this.url('/options'));
  }

  overview(query: BusinessQuery): Observable<BusinessOverview> {
    return this.http.get<BusinessOverview>(this.url(''), { params: this.params(query) });
  }

  /** BR-20: the CSV for the current selection, `compare` included. */
  export(query: BusinessQuery, compare: boolean): Observable<DownloadedCsv> {
    return this.http
      .get(this.url('/export'), {
        params: this.params(query).set('compare', compare),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(
        map((response) => toFile(response, `business-overview-${query.from}-to-${query.to}.csv`)),
      );
  }
}

function toFile(response: HttpResponse<Blob>, fallbackName: string): DownloadedCsv {
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const serverName = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1]?.trim();
  const safe = serverName !== undefined && /^[\w.-]+$/.test(serverName);
  return { blob: response.body ?? new Blob([]), fileName: safe ? serverName : fallbackName };
}
