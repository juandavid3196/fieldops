import { HttpClient, HttpEventType } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, map } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import {
  AccountPhoto,
  AccountProfile,
  ChangePasswordRequest,
  PhotoUploadEvent,
  UpdateProfileRequest,
} from '../models/account.model';

/** `/account/*` endpoints; every call acts on the session user (no identifier is ever sent). */
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path: string): string {
    return buildApiUrl(this.config, `account/${path}`);
  }

  getProfile(): Observable<AccountProfile> {
    return this.http.get<AccountProfile>(this.url('profile'));
  }

  updateProfile(request: UpdateProfileRequest): Observable<AccountProfile> {
    return this.http.put<AccountProfile>(this.url('profile'), request);
  }

  /** Uploads or replaces the photo as multipart field `file`, reporting progress. */
  uploadPhoto(file: File): Observable<PhotoUploadEvent> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http
      .put<AccountPhoto>(this.url('photo'), body, { observe: 'events', reportProgress: true })
      .pipe(
        map((event): PhotoUploadEvent | null => {
          if (event.type === HttpEventType.UploadProgress) {
            return {
              kind: 'progress',
              percent: event.total ? Math.round((event.loaded / event.total) * 100) : null,
            };
          }
          if (event.type === HttpEventType.Response && event.body !== null) {
            return { kind: 'done', photo: event.body };
          }
          return null;
        }),
        filter((event): event is PhotoUploadEvent => event !== null),
      );
  }

  removePhoto(): Observable<void> {
    return this.http.delete<void>(this.url('photo'));
  }

  changePassword(request: ChangePasswordRequest): Observable<void> {
    return this.http.post<void>(this.url('password'), request);
  }
}
