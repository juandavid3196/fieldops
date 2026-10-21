import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../config/api.config';
import { GlobalSearchResponse } from '../models/global-search.model';

export const GLOBAL_SEARCH_LIMIT = 20;

/** `GET /search/global`: the trimmed query only; never logged or stored. */
@Injectable({ providedIn: 'root' })
export class GlobalSearchService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);

  search(query: string): Observable<GlobalSearchResponse> {
    const params = new HttpParams().set('q', query).set('limit', GLOBAL_SEARCH_LIMIT);
    return this.http.get<GlobalSearchResponse>(buildApiUrl(this.config, 'search/global'), {
      params,
    });
  }
}
