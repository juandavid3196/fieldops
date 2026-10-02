import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';
import { CatalogCategory, CatalogCategoryListResponse } from '../models/catalog.model';

/** `/catalog-categories` endpoints (FR-01 to FR-05). Organization context is server-side only. */
@Injectable({ providedIn: 'root' })
export class CatalogCategoriesService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  private url(path = ''): string {
    return buildApiUrl(this.config, `catalog-categories${path}`);
  }

  list(): Observable<readonly CatalogCategory[]> {
    return this.http.get<CatalogCategoryListResponse>(this.url()).pipe(map((body) => body.items));
  }

  create(name: string): Observable<CatalogCategory> {
    return this.http.post<CatalogCategory>(this.url(), { name });
  }

  rename(id: string, name: string): Observable<CatalogCategory> {
    return this.http.put<CatalogCategory>(this.url(`/${id}`), { name });
  }

  activate(id: string): Observable<void> {
    return this.http.post<void>(this.url(`/${id}/activate`), null);
  }

  deactivate(id: string): Observable<void> {
    return this.http.post<void>(this.url(`/${id}/deactivate`), null);
  }
}
