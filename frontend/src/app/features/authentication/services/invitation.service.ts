import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import { LanguageService } from '../../../core/i18n/language.service';
import { Session } from '../../../core/models/session.model';
import { SessionService } from '../../../core/services/session.service';
import { AcceptInvitationRequest, InvitationDetails } from '../models/invitation.model';

/** Anonymous invitation endpoints; the token is only ever sent in POST bodies (BR-16). */
@Injectable({ providedIn: 'root' })
export class InvitationService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly sessionService = inject(SessionService);
  private readonly language = inject(LanguageService);

  validate(token: string): Observable<InvitationDetails> {
    return this.http.post<InvitationDetails>(buildApiUrl(this.config, 'invitations/validate'), {
      token,
    });
  }

  /**
   * Accepts as a new user; the returned session replaces the current one. The active UI language is
   * stored with the new account (app-i18n-en-es BR-08).
   */
  accept(request: AcceptInvitationRequest): Observable<Session> {
    return this.http
      .post<Session>(buildApiUrl(this.config, 'invitations/accept'), {
        ...request,
        preferredLanguage: this.language.lang(),
      })
      .pipe(tap((session) => this.sessionService.setSession(session)));
  }

  /** Accepts as the signed-in user (session cookie); the returned session replaces it. */
  acceptExisting(token: string): Observable<Session> {
    return this.http
      .post<Session>(buildApiUrl(this.config, 'invitations/accept-existing'), { token })
      .pipe(tap((session) => this.sessionService.setSession(session)));
  }
}
