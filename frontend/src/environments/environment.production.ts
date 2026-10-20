import { Environment } from './environment.model';

// The API is expected to be served from the same origin behind a reverse proxy.
export const environment: Environment = {
  production: true,
  apiBaseUrl: '/api',
  supportEmail: 'support@fieldops.app',
  primeUiLicense:
    'eyJpZCI6IjNiNjU4NWQ0LWVjNzYtNDc2My05NTZhLTA2NDIxMzRhZmQ0NiIsInByb2R1Y3QiOiJwcmltZXVpIiwidGllciI6ImNvbW11bml0eSIsInR5cGUiOiJkZXYiLCJpYXQiOjE3OTI1MjU1MTUsImV4cCI6MTgyNDA2MTUxNX0.QFYmtRG4SBHGPpl6Rahw3IlmbaQL3mNTtDYA96PQM2LEdhhJIh8O3vXlQ1Nf8tT6ZAa9jHoTl8JwHqsyCz-vCw',
};
