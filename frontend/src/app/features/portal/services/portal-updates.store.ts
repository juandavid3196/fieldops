import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { API_CONFIG } from '../../../core/config/api.config';
import { UpdatesResponse } from '../models/portal.model';
import { portalUrl } from './portal-url';

/** Unread updates count shared by the shell bell and the Updates page (BR-25). */
@Injectable({ providedIn: 'root' })
export class PortalUpdatesStore {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly count = signal(0);

  readonly unreadCount = this.count.asReadonly();

  /** `GET /portal/updates`; the count follows the response. */
  load(): Observable<UpdatesResponse> {
    return this.http
      .get<UpdatesResponse>(portalUrl(this.config, 'updates'))
      .pipe(tap((response) => this.count.set(response.unreadCount)));
  }

  /** `POST /portal/updates/seen`; the bell shows no count afterwards. */
  markSeen(): Observable<void> {
    return this.http
      .post<void>(portalUrl(this.config, 'updates/seen'), null)
      .pipe(tap(() => this.count.set(0)));
  }

  reset(): void {
    this.count.set(0);
  }
}
