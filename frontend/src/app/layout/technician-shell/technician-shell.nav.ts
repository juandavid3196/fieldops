import { TECHNICIAN_MESSAGES_PATH } from '../../features/messages/messages.routes';
import {
  ACCOUNT_PATH,
  HELP_PATH,
  PROFILE_PATH,
} from '../../features/technician-jobs/technician-jobs.routes';

/** One bottom navigation destination (BR-15). */
export interface TechnicianNavItem {
  readonly label: string;
  readonly icon: string;
  readonly link: string;
  /** The item is current when the URL path matches one of these. */
  readonly matches: (path: string) => boolean;
  /** Shows the unread messages badge (technician-messages BR-15). */
  readonly unreadMessages?: boolean;
}

export const TECHNICIAN_NAV: readonly TechnicianNavItem[] = [
  {
    label: 'Today',
    icon: 'pi-home',
    link: '/today',
    matches: (path) => path === '/today' || path.startsWith('/today/visits/'),
  },
  {
    label: 'Schedule',
    icon: 'pi-calendar',
    link: '/today/schedule',
    matches: (path) => path === '/today/schedule',
  },
  {
    label: 'Time',
    icon: 'pi-clock',
    link: '/today/time',
    matches: (path) => path === '/today/time',
  },
  {
    label: 'Messages',
    icon: 'pi-comments',
    link: TECHNICIAN_MESSAGES_PATH,
    matches: (path) => path.startsWith(TECHNICIAN_MESSAGES_PATH),
    unreadMessages: true,
  },
];

/** More menu destinations, all inside the technician shell. */
export const PROFILE_LINK = PROFILE_PATH;
export const ACCOUNT_LINK = ACCOUNT_PATH;
export const HELP_LINK = HELP_PATH;
