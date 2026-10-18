import { TECHNICIAN_MESSAGES_PATH } from '../../features/messages/messages.routes';

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

/** The bell opens Messages and shows the same unread badge (technician-messages BR-15). */
export const NOTIFICATIONS_LINK = TECHNICIAN_MESSAGES_PATH;
export const PROFILE_LINK = '/team';
export const ACCOUNT_LINK = '/account';
export const HELP_LINK = '/help';
