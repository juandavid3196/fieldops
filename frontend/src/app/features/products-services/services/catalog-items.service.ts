import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  CatalogDetail,
  CatalogFilters,
  CatalogImage,
  CatalogItemRequest,
  CatalogListQuery,
  CatalogListResponse,
  CatalogSummary,
  CsvFile,
  ImportResult,
  PAGE_SIZE,
} from '../models/catalog.model';

function filterParams(filters: CatalogFilters): HttpParams {
  let params = new HttpParams().set('sort', filters.sort);
  const search = filters.search.trim();
  if (search.length > 0) {
    params = params.set('search', search);
  }
  if (filters.type !== null) {
    params = params.set('type', filters.type);
  }
  if (filters.taxStatus !== null) {
    params = params.set('taxStatus', filters.taxStatus);
  }
  if (filters.status !== null) {
    params = params.set('status', filters.status);
  }
  return params;
}

function isoDate(now: Date): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** `/catalog-items` endpoints (FR-02 to FR-14). Organization context is server-side only. */
@Injectable({ providedIn: 'root' })
export class CatalogItemsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path = ''): string {
    return buildApiUrl(this.config, `catalog-items${path}`);
  }

  list(query: CatalogListQuery): Observable<CatalogListResponse> {
    const params = filterParams(query).set('page', query.page).set('pageSize', PAGE_SIZE);
    return this.http.get<CatalogListResponse>(this.url(), { params });
  }

  summary(): Observable<CatalogSummary> {
    return this.http.get<CatalogSummary>(this.url('/summary'));
  }

  get(id: string): Observable<CatalogDetail> {
    return this.http.get<CatalogDetail>(this.url(`/${id}`));
  }

  create(body: CatalogItemRequest): Observable<CatalogDetail> {
    return this.http.post<CatalogDetail>(this.url(), body);
  }

  update(id: string, body: CatalogItemRequest): Observable<CatalogDetail> {
    return this.http.put<CatalogDetail>(this.url(`/${id}`), body);
  }

  activate(id: string): Observable<void> {
    return this.http.post<void>(this.url(`/${id}/activate`), null);
  }

  deactivate(id: string): Observable<void> {
    return this.http.post<void>(this.url(`/${id}/deactivate`), null);
  }

  getImage(id: string): Observable<Blob> {
    return this.http.get(this.url(`/${id}/image`), { responseType: 'blob' });
  }

  putImage(id: string, file: File): Observable<CatalogImage> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http.put<CatalogImage>(this.url(`/${id}/image`), body);
  }

  deleteImage(id: string): Observable<void> {
    return this.http.delete<void>(this.url(`/${id}/image`));
  }

  /** CSV of every item matching the filters and sort (BR-13); no paging. */
  export(filters: CatalogFilters): Observable<CsvFile> {
    return this.http
      .get(this.url('/export'), {
        params: filterParams(filters),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(map((response) => toCsv(response, `products-services-${isoDate(new Date())}.csv`)));
  }

  importTemplate(): Observable<CsvFile> {
    return this.http
      .get(this.url('/import-template'), { responseType: 'blob', observe: 'response' })
      .pipe(map((response) => toCsv(response, 'products-services-template.csv')));
  }

  import(file: File): Observable<ImportResult> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http.post<ImportResult>(this.url('/import'), body);
  }
}

function toCsv(response: HttpResponse<Blob>, fallbackName: string): CsvFile {
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const serverName = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1]?.trim();
  const safe = serverName !== undefined && /^[\w.-]+$/.test(serverName);
  return { blob: response.body ?? new Blob([]), fileName: safe ? serverName : fallbackName };
}
