import { Environment } from './environment.model';

// Development defaults. Replaced by environment.production.ts in production builds.
export const environment: Environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5034',
};
