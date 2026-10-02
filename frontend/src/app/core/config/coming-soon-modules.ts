/** BR-02: slug to module name for every "Coming soon" destination. */
export const COMING_SOON_MODULES: Readonly<Record<string, string>> = {
  quotes: 'Quotes',
  'work-orders': 'Work orders',
  schedule: 'Schedule',
  team: 'Team',
  'team-skills': 'Team skills',
  'team-availability': 'Team availability',
  'technician-profile': 'Technician profile',
  'technician-job-history': 'Job history',
  invoices: 'Invoices',
  reports: 'Reports',
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
