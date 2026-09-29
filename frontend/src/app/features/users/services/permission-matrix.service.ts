import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import { PermissionMatrix } from '../models/users.model';

/** `GET /permission-matrix` (FR-05): the read-only role and module catalog. */
@Injectable({ providedIn: 'root' })
export class PermissionMatrixService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  get(): Observable<PermissionMatrix> {
    return this.http.get<PermissionMatrix>(buildApiUrl(this.config, 'permission-matrix'));
  }
}
