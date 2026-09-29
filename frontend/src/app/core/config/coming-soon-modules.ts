/** BR-02: slug to module name for every "Coming soon" destination. */
export const COMING_SOON_MODULES: Readonly<Record<string, string>> = {
  requests: 'Requests',
  quotes: 'Quotes',
  'work-orders': 'Work orders',
  schedule: 'Schedule',
  customers: 'Customers',
  team: 'Team',
  'products-and-services': 'Products and services',
  invoices: 'Invoices',
  reports: 'Reports',
  'users-and-permissions': 'Users & permissions',
  'business-hours': 'Business hours',
  notifications: 'Notifications',
  help: 'Help',
  search: 'Search',
  'tax-rates': 'Tax rates',
};

/** Own-property lookup (never inherited keys such as `constructor`). */
export function comingSoonModuleName(slug: string): string | null {
  return Object.hasOwn(COMING_SOON_MODULES, slug) ? COMING_SOON_MODULES[slug] : null;
}

export function comingSoonPath(slug: string): string {
  return `/coming-soon/${slug}`;
}
