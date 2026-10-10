import { HttpParams } from '@angular/common/http';

import { ApiConfig, buildApiUrl } from '../../../core/config/api.config';

/** Absolute URL of a `/portal/...` endpoint. Resource ids go in the path, never in a body. */
export function portalUrl(config: ApiConfig, path: string): string {
  return buildApiUrl(config, `portal/${path}`);
}

/** List query: `page` (≥ 1) plus the optional property filter; absent property means All. */
export function listParams(
  page: number,
  propertyId: string | null,
  extra: Readonly<Record<string, string>> = {},
): HttpParams {
  let params = new HttpParams().set('page', page);
  if (propertyId !== null) {
    params = params.set('propertyId', propertyId);
  }
  for (const [key, value] of Object.entries(extra)) {
    params = params.set(key, value);
  }
  return params;
}

export function encode(id: string): string {
  return encodeURIComponent(id);
}
