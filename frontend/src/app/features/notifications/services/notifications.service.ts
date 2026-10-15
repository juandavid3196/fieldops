import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import { NotificationUnreadService } from '../../../core/services/notification-unread.service';
import {
  NotificationsOptions,
  NotificationsPage,
  NotificationsRequest,
  NotificationsSummary,
  ReadAllResult,
  ReadResult,
} from '../models/notifications.model';

/** `/notifications` endpoints (Final contract: specs/notifications-center/spec.md). Scope is server-side only. */
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly unread = inject(NotificationUnreadService);

  private url(path = ''): string {
    return buildApiUrl(this.config, `notifications${path}`);
  }

  options(): Observable<NotificationsOptions> {
    return this.http.get<NotificationsOptions>(this.url('/options'));
  }

  /** BR-16: every summary response also feeds the bell. */
  summary(): Observable<NotificationsSummary> {
    return this.http
      .get<NotificationsSummary>(this.url('/summary'))
      .pipe(tap((summary) => this.unread.set(summary.unread)));
  }

  list(query: NotificationsRequest): Observable<NotificationsPage> {
    let params = new HttpParams()
      .set('tab', query.tab)
      .set('date', query.date)
      .set('page', query.page)
      .set('pageSize', query.pageSize);
    for (const [key, value] of [
      ['q', query.q],
      ['type', query.type],
      ['branchId', query.branchId],
    ] as const) {
      if (value !== null && value !== '') {
        params = params.set(key, value);
      }
    }
    return this.http.get<NotificationsPage>(this.url(), { params });
  }

  read(id: string): Observable<ReadResult> {
    return this.mark(`/${encodeURIComponent(id)}/read`);
  }

  unreadOne(id: string): Observable<ReadResult> {
    return this.mark(`/${encodeURIComponent(id)}/unread`);
  }

  readAll(): Observable<ReadAllResult> {
    return this.mark('/read-all');
  }

  /** BR-16: the response updates the bell with no extra request. */
  private mark<T extends ReadResult>(path: string): Observable<T> {
    return this.http
      .post<T>(this.url(path), null)
      .pipe(tap((result) => this.unread.set(result.summary.unread)));
  }
}
