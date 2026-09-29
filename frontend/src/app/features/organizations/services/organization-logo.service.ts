import { HttpClient, HttpEventType } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, map } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import { OrganizationLogoMetadata } from '../models/company-settings.model';

/** Upload progress (`percent` is `null` when the size is unknown) or the final metadata. */
export type LogoUploadEvent =
  | { readonly kind: 'progress'; readonly percent: number | null }
  | { readonly kind: 'done'; readonly logo: OrganizationLogoMetadata };

/** `GET`/`PUT`/`DELETE /organization-settings/logo` (BR-07, BR-08). Bytes are never inspected here. */
@Injectable({ providedIn: 'root' })
export class OrganizationLogoService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(): string {
    return buildApiUrl(this.config, 'organization-settings/logo');
  }

  /** The stored logo bytes (`404` when none). */
  get(): Observable<Blob> {
    return this.http.get(this.url(), { responseType: 'blob' });
  }

  /** Uploads or replaces the logo as multipart field `file`, reporting progress. */
  upload(file: File): Observable<LogoUploadEvent> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http
      .put<OrganizationLogoMetadata>(this.url(), body, { observe: 'events', reportProgress: true })
      .pipe(
        map((event): LogoUploadEvent | null => {
          if (event.type === HttpEventType.UploadProgress) {
            return {
              kind: 'progress',
              percent: event.total ? Math.round((event.loaded / event.total) * 100) : null,
            };
          }
          if (event.type === HttpEventType.Response && event.body !== null) {
            return { kind: 'done', logo: event.body };
          }
          return null;
        }),
        filter((event): event is LogoUploadEvent => event !== null),
      );
  }

  remove(): Observable<void> {
    return this.http.delete<void>(this.url());
  }
}
