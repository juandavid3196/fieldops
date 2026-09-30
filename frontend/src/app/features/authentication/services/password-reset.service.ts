import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import { PasswordResetDetails } from '../models/password-reset.model';

/** Anonymous password-reset endpoints; the token is only ever sent in POST bodies (BR-20). */
@Injectable({ providedIn: 'root' })
export class PasswordResetService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  /** Always `202` with an empty body when accepted (BR-06). */
  request(email: string): Observable<void> {
    return this.http.post<void>(buildApiUrl(this.config, 'password-resets'), { email });
  }

  validate(token: string): Observable<PasswordResetDetails> {
    return this.http.post<PasswordResetDetails>(
      buildApiUrl(this.config, 'password-resets/validate'),
      { token },
    );
  }

  /** `204` on success; it creates no session. */
  confirm(token: string, password: string): Observable<void> {
    return this.http.post<void>(buildApiUrl(this.config, 'password-resets/confirm'), {
      token,
      password,
    });
  }
}
