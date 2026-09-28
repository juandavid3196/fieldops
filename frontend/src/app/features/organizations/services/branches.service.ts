import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  BranchDetail,
  BranchListResponse,
  BranchRequest,
  BranchUpdateRequest,
} from '../models/company-settings.model';

/** `/branches` endpoints (FR-05 to FR-09). */
@Injectable({ providedIn: 'root' })
export class BranchesService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  list(): Observable<BranchListResponse> {
    return this.http.get<BranchListResponse>(buildApiUrl(this.config, 'branches'));
  }

  get(id: string): Observable<BranchDetail> {
    return this.http.get<BranchDetail>(buildApiUrl(this.config, `branches/${id}`));
  }

  create(request: BranchRequest): Observable<BranchDetail> {
    return this.http.post<BranchDetail>(buildApiUrl(this.config, 'branches'), request);
  }

  update(id: string, request: BranchUpdateRequest): Observable<BranchDetail> {
    return this.http.put<BranchDetail>(buildApiUrl(this.config, `branches/${id}`), request);
  }

  deactivate(id: string): Observable<void> {
    return this.http.post<void>(buildApiUrl(this.config, `branches/${id}/deactivate`), null);
  }

  reactivate(id: string): Observable<void> {
    return this.http.post<void>(buildApiUrl(this.config, `branches/${id}/reactivate`), null);
  }
}
