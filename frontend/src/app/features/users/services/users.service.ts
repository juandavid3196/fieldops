import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  AccessRequest,
  InviteRequest,
  PAGE_SIZE,
  UserListQuery,
  UserListResponse,
  UserRow,
  UserSummary,
} from '../models/users.model';

/** `/users` endpoints (FR-02, FR-03, FR-06, FR-08, FR-10, FR-11). */
@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  list(query: UserListQuery): Observable<UserListResponse> {
    let params = new HttpParams().set('sort', query.sort).set('page', query.page);
    params = params.set('pageSize', PAGE_SIZE);
    const search = query.search.trim();
    if (search.length > 0) {
      params = params.set('search', search);
    }
    if (query.roleCode !== null) {
      params = params.set('roleCode', query.roleCode);
    }
    if (query.branchId !== null) {
      params = params.set('branchId', query.branchId);
    }
    if (query.status !== null) {
      params = params.set('status', query.status);
    }
    return this.http.get<UserListResponse>(buildApiUrl(this.config, 'users'), { params });
  }

  summary(): Observable<UserSummary> {
    return this.http.get<UserSummary>(buildApiUrl(this.config, 'users/summary'));
  }

  invite(request: InviteRequest): Observable<UserRow> {
    return this.http.post<UserRow>(buildApiUrl(this.config, 'users/invitations'), request);
  }

  resend(id: string): Observable<UserRow> {
    return this.http.post<UserRow>(
      buildApiUrl(this.config, `users/invitations/${id}/resend`),
      null,
    );
  }

  revoke(id: string): Observable<void> {
    return this.http.post<void>(buildApiUrl(this.config, `users/invitations/${id}/revoke`), null);
  }

  /** Routes to the member or invitation endpoint by row kind. */
  updateAccess(row: Pick<UserRow, 'id' | 'kind'>, body: AccessRequest): Observable<UserRow> {
    const path =
      row.kind === 'invitation' ? `users/invitations/${row.id}/access` : `users/${row.id}/access`;
    return this.http.put<UserRow>(buildApiUrl(this.config, path), body);
  }

  suspend(id: string): Observable<void> {
    return this.http.post<void>(buildApiUrl(this.config, `users/${id}/suspend`), null);
  }

  reactivate(id: string): Observable<void> {
    return this.http.post<void>(buildApiUrl(this.config, `users/${id}/reactivate`), null);
  }
}
