import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { API_CONFIG } from '../../../core/config/api.config';
import { LanguageService } from '../../../core/i18n/language.service';
import { PortalSession } from '../../../core/models/portal-session.model';
import { PortalSessionService } from '../../../core/services/portal-session.service';
import { portalUrl } from './portal-url';

/** Body of `POST /portal/invitations/validate` (BR-12). It carries no identifiers. */
export interface PortalInvitationDetails {
  readonly organizationName: string;
  readonly firstName: string;
  readonly email: string;
  readonly accountExists: boolean;
  readonly lastNameRequired: boolean;
}

/** Anonymous portal invitation endpoints; the token is only ever sent in POST bodies (BR-12). */
@Injectable({ providedIn: 'root' })
export class PortalInvitationService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly session = inject(PortalSessionService);
  private readonly language = inject(LanguageService);

  validate(token: string): Observable<PortalInvitationDetails> {
    return this.http.post<PortalInvitationDetails>(portalUrl(this.config, 'invitations/validate'), {
      token,
    });
  }

  /** New user; `lastName` is sent only when the page asked for it. */
  accept(token: string, password: string, lastName: string | null): Observable<PortalSession> {
    const preferredLanguage = this.language.lang();
    const body =
      lastName === null
        ? { token, password, preferredLanguage }
        : { token, password, lastName, preferredLanguage };
    return this.http
      .post<PortalSession>(portalUrl(this.config, 'invitations/accept'), body)
      .pipe(tap((session) => this.session.setSession(session)));
  }

  /** Existing user proving their password; it never carries a last name. */
  acceptExisting(token: string, password: string): Observable<PortalSession> {
    return this.http
      .post<PortalSession>(portalUrl(this.config, 'invitations/accept-existing'), {
        token,
        password,
      })
      .pipe(tap((session) => this.session.setSession(session)));
  }
}
