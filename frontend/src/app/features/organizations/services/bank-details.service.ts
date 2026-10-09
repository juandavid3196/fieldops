import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import { BankDetails, BankDetailsRequest } from '../models/bank-details.model';

/** `GET`/`PUT /organization-settings/bank-details` (Owner only, BR-25). */
@Injectable({ providedIn: 'root' })
export class BankDetailsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(): string {
    return buildApiUrl(this.config, 'organization-settings/bank-details');
  }

  get(): Observable<BankDetails> {
    return this.http.get<BankDetails>(this.url());
  }

  update(request: BankDetailsRequest): Observable<BankDetails> {
    return this.http.put<BankDetails>(this.url(), request);
  }
}
