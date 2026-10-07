import { technicianComingSoonPath } from '../../core/config/coming-soon-modules';

/** One bottom navigation destination (BR-15). */
export interface TechnicianNavItem {
  readonly label: string;
  readonly icon: string;
  readonly link: string;
  /** The item is current when the URL path matches one of these. */
  readonly matches: (path: string) => boolean;
}

function soon(label: string, icon: string, slug: string): TechnicianNavItem {
  const link = technicianComingSoonPath(slug);
  return { label, icon, link, matches: (path) => path === link };
}

export const TECHNICIAN_NAV: readonly TechnicianNavItem[] = [
  {
    label: 'Today',
    icon: 'pi-home',
    link: '/today',
    matches: (path) => path === '/today' || path.startsWith('/today/visits/'),
  },
  soon('Schedule', 'pi-calendar', 'schedule'),
  soon('Time', 'pi-clock', 'time'),
  soon('Messages', 'pi-comments', 'messages'),
];

export const NOTIFICATIONS_LINK = technicianComingSoonPath('notifications');
export const PROFILE_LINK = '/team';
