import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, switchMap, tap, throwError } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../config/api.config';
import { LanguageService } from '../i18n/language.service';
import { isApiError } from '../models/api-error.model';
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
  private readonly language = inject(LanguageService);
  private readonly currentSession = signal<Session | null>(null);

  readonly session = this.currentSession.asReadonly();

  /** Loads the session from `GET /sessions/current`; clears it on any failure. */
  loadCurrent(): Observable<Session> {
    return this.http.get<Session>(buildApiUrl(this.config, 'sessions/current')).pipe(
      tap({
        next: (session) => this.currentSession.set(session),
        error: () => this.currentSession.set(null),
      }),
      switchMap((session) => this.applyLanguage(session)),
    );
  }

  /**
   * Reloads the session without clearing it when the reload fails, for refreshes that must not
   * sign the user out (profile name and photo). Failures are swallowed; the next request decides.
   */
  refreshCurrent(): Observable<void> {
    return this.http.get<Session>(buildApiUrl(this.config, 'sessions/current')).pipe(
      tap((session) => this.currentSession.set(session)),
      switchMap((session) => this.applyLanguage(session)),
      map(() => undefined),
      catchError(() => of(undefined)),
    );
  }

  /** Signs in with `POST /sessions`; the email is trimmed and lowercased (BR-01). */
  signIn(request: SignInRequest): Observable<Session> {
    const body: SignInRequest = {
      email: request.email.trim().toLowerCase(),
      password: request.password,
      rememberMe: request.rememberMe,
    };

    return this.http.post<Session>(buildApiUrl(this.config, 'sessions'), body).pipe(
      tap((session) => this.currentSession.set(session)),
      switchMap((session) => this.applyLanguage(session)),
    );
  }

  /** Stores a session body issued by another endpoint (invitation acceptance). */
  setSession(session: Session): void {
    this.currentSession.set(session);
  }

  /**
   * Signs out with `DELETE /sessions/current`. `204` and `401` (already signed
   * out) clear the session; any other failure keeps it and is rethrown.
   */
  signOut(): Observable<void> {
    return this.http.delete<unknown>(buildApiUrl(this.config, 'sessions/current')).pipe(
      map(() => undefined),
      catchError((error: unknown) => {
        if (isApiError(error) && error.status === 401) {
          return of(undefined);
        }
        return throwError(() => error);
      }),
      tap(() => this.currentSession.set(null)),
    );
  }

  /** BR-02: the session `preferredLanguage` wins over the local value; unsupported values are ignored. */
  private applyLanguage(session: Session): Observable<Session> {
    return this.language.applyPreference(session.user.preferredLanguage).pipe(map(() => session));
  }
}
