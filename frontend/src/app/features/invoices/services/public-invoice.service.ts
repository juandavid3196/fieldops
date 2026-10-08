import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import { PublicInvoice } from '../models/invoice.model';
import { PublicInvoiceTokenService } from './public-invoice-token.service';

/** Anonymous invoice link endpoints; the token travels only in JSON POST bodies (BR-28). */
@Injectable({ providedIn: 'root' })
export class PublicInvoiceService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly tokens = inject(PublicInvoiceTokenService);

  private url(path: string): string {
    return buildApiUrl(this.config, `public/invoice-links/${path}`);
  }

  private body(): { token: string } {
    return { token: this.tokens.read() ?? '' };
  }

  view(): Observable<PublicInvoice> {
    return this.http.post<PublicInvoice>(this.url('view'), this.body());
  }

  logo(): Observable<Blob> {
    return this.http.post(this.url('logo'), this.body(), { responseType: 'blob' });
  }

  pdf(): Observable<Blob> {
    return this.http.post(this.url('pdf'), this.body(), { responseType: 'blob' });
  }
}
