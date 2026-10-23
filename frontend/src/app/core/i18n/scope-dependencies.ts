/**
 * Feature scopes whose texts a feature also renders, from its imports of other features'
 * components, services and utilities. A route loads its scope and the transitive closure of this map
 * (BR-20). Roots: `ROOT` is the authenticated app shell.
 */
export const I18N_SCOPE_DEPENDENCIES: Readonly<Record<string, readonly string[]>> = {
  ROOT: ['invoices', 'jobs', 'notifications', 'quotes', 'requests'],
  account: ['organizations'],
  'audit-log': ['organizations'],
  authentication: ['organizations'],
  'billing-review': ['organizations'],
  'business-overview': ['dashboard', 'organizations', 'schedule'],
  customers: ['invoices', 'jobs', 'organizations', 'quotes', 'requests'],
  dashboard: ['business-overview', 'organizations', 'schedule'],
  invoices: ['billing-review', 'organizations', 'requests'],
  jobs: ['customers', 'organizations', 'requests'],
  messages: ['technician-jobs'],
  notifications: ['organizations'],
  portal: [
    'authentication',
    'customers',
    'help',
    'invoices',
    'organizations',
    'quotes',
    'service-request',
  ],
  'products-services': ['organizations'],
  quotes: [
    'customers',
    'jobs',
    'organizations',
    'products-services',
    'requests',
    'service-request',
  ],
  reports: ['business-overview', 'dashboard', 'organizations', 'schedule'],
  requests: ['customers', 'organizations', 'quotes', 'service-request'],
  schedule: ['jobs', 'organizations', 'technician-jobs'],
  team: ['organizations'],
  'technician-jobs': ['coming-soon', 'messages', 'requests'],
  users: ['organizations'],
};
