import { InjectionToken } from '@angular/core';

import { environment } from '../../../environments/environment';

export interface ApiConfig {
  /** Base URL of the FieldOps API, without trailing slash. */
  readonly baseUrl: string;
}

export const API_CONFIG = new InjectionToken<ApiConfig>('API_CONFIG', {
  providedIn: 'root',
  factory: () => ({ baseUrl: trimTrailingSlashes(environment.apiBaseUrl) }),
});

/** Joins the API base URL with a relative endpoint path. */
export function buildApiUrl(config: ApiConfig, path: string): string {
  return `${trimTrailingSlashes(config.baseUrl)}/${path.replace(/^\/+/, '')}`;
}

/** Whether a request URL targets the FieldOps API rather than a third party or static asset. */
export function isApiUrl(config: ApiConfig, url: string): boolean {
  const baseUrl = trimTrailingSlashes(config.baseUrl);
  return url === baseUrl || url.startsWith(`${baseUrl}/`);
}

function trimTrailingSlashes(value: string): string {
  return value.replace(/\/+$/, '');
}
