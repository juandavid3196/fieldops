import { Environment } from './environment.model';

// The API is expected to be served from the same origin behind a reverse proxy.
export const environment: Environment = {
  production: true,
  apiBaseUrl: '/api',
};
