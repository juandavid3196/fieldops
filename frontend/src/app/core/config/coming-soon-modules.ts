import { localized, t } from '../i18n/translate';
/** BR-02: slug to module name for every "Coming soon" destination. */
export const COMING_SOON_MODULES: Readonly<Record<string, string>> = localized(() => ({
  quotes: t('shell.comingSoonModules.quotes'),
  'work-orders': t('shell.comingSoonModules.workOrders'),
  team: t('shell.comingSoonModules.team'),
  'team-skills': t('shell.comingSoonModules.teamSkills'),
  'team-availability': t('shell.comingSoonModules.teamAvailability'),
  'technician-profile': t('shell.comingSoonModules.technicianProfile'),
  'technician-job-history': t('shell.comingSoonModules.jobHistory'),
  invoices: t('shell.comingSoonModules.invoices'),
  'invoice-drafts': t('shell.comingSoonModules.drafts'),
  'invoice-sent': t('shell.comingSoonModules.sent'),
  'invoice-payments': t('shell.comingSoonModules.payments'),
  'invoice-overdue': t('shell.comingSoonModules.overdue'),
  notifications: t('shell.comingSoonModules.notifications'),
  help: t('shell.comingSoonModules.help'),
  search: t('common.search'),
  'tax-rates': t('shell.comingSoonModules.taxRates'),
  schedule: t('shell.comingSoonModules.schedule'),
  time: t('shell.comingSoonModules.time'),
  messages: t('shell.comingSoonModules.messages'),
}));

/** Own-property lookup (never inherited keys such as `constructor`). */
export function comingSoonModuleName(slug: string): string | null {
  return Object.hasOwn(COMING_SOON_MODULES, slug) ? COMING_SOON_MODULES[slug] : null;
}

export function comingSoonPath(slug: string): string {
  return `/coming-soon/${slug}`;
}

/** Coming soon destinations inside the technician shell. */
export function technicianComingSoonPath(slug: string): string {
  return `/today/soon/${slug}`;
}
