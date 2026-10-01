import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  ActivityResponse,
  AppointmentsResponse,
  BranchOptionsResponse,
  CsvFile,
  CustomerDetail,
  CustomerOverview,
  CustomerListQuery,
  CustomerListResponse,
  CustomerMetrics,
  CustomerRequest,
  CustomerTag,
  DuplicateCheckRequest,
  DuplicateCheckResponse,
  ImportPreview,
  ImportResult,
  NoteItem,
  NotesResponse,
  PropertiesResponse,
  PropertyItem,
  PropertyRequest,
  RecentWorkResponse,
} from '../models/customer.model';

/** `/customers` and `/customer-tags` endpoints. Organization and branch scope are server-side only. */
@Injectable({ providedIn: 'root' })
export class CustomersService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path: string): string {
    return buildApiUrl(this.config, path);
  }

  metrics(branchId: string | null): Observable<CustomerMetrics> {
    let params = new HttpParams();
    if (branchId !== null) {
      params = params.set('branchId', branchId);
    }
    return this.http.get<CustomerMetrics>(this.url('customers/metrics'), { params });
  }

  branchOptions(): Observable<BranchOptionsResponse> {
    return this.http.get<BranchOptionsResponse>(this.url('customers/branch-options'));
  }

  list(query: CustomerListQuery): Observable<CustomerListResponse> {
    let params = new HttpParams().set('tab', query.tab).set('sort', query.sort);
    const search = query.search.trim();
    if (search.length > 0) {
      params = params.set('search', search);
    }
    if (query.type !== null) {
      params = params.set('type', query.type);
    }
    if (query.branchId !== null) {
      params = params.set('branchId', query.branchId);
    }
    for (const tagId of query.tagIds) {
      params = params.append('tagIds', tagId);
    }
    if (query.balanceStatus !== 'any') {
      params = params.set('balanceStatus', query.balanceStatus);
    }
    params = params.set('page', query.page);
    return this.http.get<CustomerListResponse>(this.url('customers'), { params });
  }

  get(id: string): Observable<CustomerDetail> {
    return this.http.get<CustomerDetail>(this.url(`customers/${id}`));
  }

  create(body: CustomerRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(this.url('customers'), body);
  }

  update(id: string, body: CustomerRequest): Observable<CustomerDetail> {
    return this.http.put<CustomerDetail>(this.url(`customers/${id}`), body);
  }

  archive(id: string): Observable<void> {
    return this.http.post<void>(this.url(`customers/${id}/archive`), null);
  }

  reactivate(id: string): Observable<void> {
    return this.http.post<void>(this.url(`customers/${id}/reactivate`), null);
  }

  overview(id: string): Observable<CustomerOverview> {
    return this.http.get<CustomerOverview>(this.url(`customers/${id}/detail`));
  }

  properties(id: string): Observable<PropertiesResponse> {
    return this.http.get<PropertiesResponse>(this.url(`customers/${id}/properties`));
  }

  property(id: string, propertyId: string): Observable<PropertyItem> {
    return this.http.get<PropertyItem>(this.url(`customers/${id}/properties/${propertyId}`));
  }

  createProperty(id: string, body: PropertyRequest): Observable<PropertyItem> {
    return this.http.post<PropertyItem>(this.url(`customers/${id}/properties`), body);
  }

  updateProperty(id: string, propertyId: string, body: PropertyRequest): Observable<PropertyItem> {
    return this.http.put<PropertyItem>(this.url(`customers/${id}/properties/${propertyId}`), body);
  }

  setPrimaryProperty(id: string, propertyId: string): Observable<void> {
    return this.propertyAction(id, propertyId, 'set-primary');
  }

  archiveProperty(id: string, propertyId: string): Observable<void> {
    return this.propertyAction(id, propertyId, 'archive');
  }

  reactivateProperty(id: string, propertyId: string): Observable<void> {
    return this.propertyAction(id, propertyId, 'reactivate');
  }

  private propertyAction(id: string, propertyId: string, action: string): Observable<void> {
    return this.http.post<void>(
      this.url(`customers/${id}/properties/${propertyId}/${action}`),
      null,
    );
  }

  recentWork(id: string): Observable<RecentWorkResponse> {
    return this.http.get<RecentWorkResponse>(this.url(`customers/${id}/recent-work`));
  }

  upcomingAppointments(id: string): Observable<AppointmentsResponse> {
    return this.http.get<AppointmentsResponse>(this.url(`customers/${id}/upcoming-appointments`));
  }

  notes(id: string, page: number): Observable<NotesResponse> {
    return this.http.get<NotesResponse>(this.url(`customers/${id}/notes`), {
      params: new HttpParams().set('page', page),
    });
  }

  addNote(id: string, note: string): Observable<NoteItem> {
    return this.http.post<NoteItem>(this.url(`customers/${id}/notes`), { note });
  }

  activity(id: string, page: number): Observable<ActivityResponse> {
    return this.http.get<ActivityResponse>(this.url(`customers/${id}/activity`), {
      params: new HttpParams().set('page', page),
    });
  }

  duplicateCheck(body: DuplicateCheckRequest): Observable<DuplicateCheckResponse> {
    return this.http.post<DuplicateCheckResponse>(this.url('customers/duplicate-check'), body);
  }

  tags(): Observable<readonly CustomerTag[]> {
    return this.http.get<readonly CustomerTag[]>(this.url('customer-tags'));
  }

  createTag(name: string): Observable<CustomerTag> {
    return this.http.post<CustomerTag>(this.url('customer-tags'), { name });
  }

  importTemplate(): Observable<CsvFile> {
    return this.http
      .get(this.url('customers/import/template'), { responseType: 'blob', observe: 'response' })
      .pipe(map((response) => toCsv(response, 'customers-template.csv')));
  }

  importPreview(file: File): Observable<ImportPreview> {
    return this.http.post<ImportPreview>(this.url('customers/import/preview'), formData(file));
  }

  import(file: File): Observable<ImportResult> {
    return this.http.post<ImportResult>(this.url('customers/import'), formData(file));
  }
}

function formData(file: File): FormData {
  const body = new FormData();
  body.append('file', file, file.name);
  return body;
}

function toCsv(response: HttpResponse<Blob>, fallbackName: string): CsvFile {
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const serverName = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1]?.trim();
  const safe = serverName !== undefined && /^[\w.-]+$/.test(serverName);
  return { blob: response.body ?? new Blob([]), fileName: safe ? serverName : fallbackName };
}
