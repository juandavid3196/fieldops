import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, tap, throwError } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../config/api.config';
import { isApiError } from '../models/api-error.model';
import { PortalSession, PortalSignInRequest } from '../models/portal-session.model';

/**
 * Holds the portal cookie session, separate from the internal {@link SessionService}. The backend
 * decides authentication; this state only drives the UI.
 */
@Injectable({ providedIn: 'root' })
export class PortalSessionService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly currentSession = signal<PortalSession | null>(null);
  private readonly expired = signal(false);

  readonly session = this.currentSession.asReadonly();
  /** `true` once an in-memory session ended by a `401`; sign-in shows it once, then clears it. */
  readonly expiredNotice = this.expired.asReadonly();

  /** Loads `GET /portal/sessions/current`; clears the session on failure (notice only if one existed). */
  loadCurrent(): Observable<PortalSession> {
    return this.http.get<PortalSession>(buildApiUrl(this.config, 'portal/sessions/current')).pipe(
      tap({
        next: (session) => this.currentSession.set(session),
        error: (error: unknown) => {
          if (isApiError(error) && error.status === 401 && this.currentSession() !== null) {
            this.expired.set(true);
          }
          this.currentSession.set(null);
        },
      }),
    );
  }

  signIn(request: PortalSignInRequest): Observable<PortalSession> {
    const body: PortalSignInRequest = {
      email: request.email.trim().toLowerCase(),
      password: request.password,
      rememberMe: request.rememberMe,
    };
    return this.http
      .post<PortalSession>(buildApiUrl(this.config, 'portal/sessions'), body)
      .pipe(tap((session) => this.currentSession.set(session)));
  }

  /** Stores a session body issued by another endpoint (invitation acceptance). */
  setSession(session: PortalSession): void {
    this.currentSession.set(session);
  }

  /** `POST /portal/sessions/current/account`; the reissued session replaces the current one. */
  switchAccount(contactId: string): Observable<PortalSession> {
    return this.http
      .post<PortalSession>(buildApiUrl(this.config, 'portal/sessions/current/account'), {
        contactId,
      })
      .pipe(tap((session) => this.currentSession.set(session)));
  }

  /** `204` and `401` clear the session; any other failure keeps it and is rethrown. */
  signOut(): Observable<void> {
    return this.http.delete<unknown>(buildApiUrl(this.config, 'portal/sessions/current')).pipe(
      map(() => undefined),
      catchError((error: unknown) =>
        isApiError(error) && error.status === 401 ? of(undefined) : throwError(() => error),
      ),
      tap(() => this.currentSession.set(null)),
    );
  }

  /** A `401` on a portal request while a session was in memory (the interceptor calls this). */
  expire(): void {
    this.currentSession.set(null);
    this.expired.set(true);
  }

  clearExpiredNotice(): void {
    this.expired.set(false);
  }
}
