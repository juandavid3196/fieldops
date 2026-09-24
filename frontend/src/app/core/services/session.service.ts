import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, map, tap } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../config/api.config';
import { Session, SignInRequest } from '../models/session.model';

/**
 * Holds the current cookie session. The backend decides authentication; this
 * state only drives the UI. Failures are rethrown as `ApiError` by the
 * error interceptor.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly currentSession = signal<Session | null>(null);

  readonly session = this.currentSession.asReadonly();

  /** Loads the session from `GET /sessions/current`; clears it on any failure. */
  loadCurrent(): Observable<Session> {
    return this.http.get<Session>(buildApiUrl(this.config, 'sessions/current')).pipe(
      tap({
        next: (session) => this.currentSession.set(session),
        error: () => this.currentSession.set(null),
      }),
    );
  }

  /** Signs in with `POST /sessions`; the email is trimmed and lowercased (BR-01). */
  signIn(request: SignInRequest): Observable<Session> {
    const body: SignInRequest = {
      email: request.email.trim().toLowerCase(),
      password: request.password,
      rememberMe: request.rememberMe,
    };

    return this.http
      .post<Session>(buildApiUrl(this.config, 'sessions'), body)
      .pipe(tap((session) => this.currentSession.set(session)));
  }

  /** Signs out with `DELETE /sessions/current`; the session is kept if it fails. */
  signOut(): Observable<void> {
    return this.http.delete<unknown>(buildApiUrl(this.config, 'sessions/current')).pipe(
      tap(() => this.currentSession.set(null)),
      map(() => undefined),
    );
  }
}
