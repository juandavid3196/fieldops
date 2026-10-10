/** One primary navigation destination of the portal shell (BR-16). */
export interface PortalNavItem {
  readonly label: string;
  readonly link: string;
  /** Home matches only `/portal`; the other sections also match their detail pages. */
  readonly exact: boolean;
}

export const PORTAL_NAV: readonly PortalNavItem[] = [
  { label: 'Home', link: '/portal', exact: true },
  { label: 'Requests', link: '/portal/requests', exact: false },
  { label: 'Appointments', link: '/portal/appointments', exact: false },
  { label: 'Quotes', link: '/portal/quotes', exact: false },
  { label: 'Invoices', link: '/portal/invoices', exact: false },
];

export const UPDATES_LINK = '/portal/updates';
export const HELP_FRAGMENT = 'help';
export const SIGN_IN_LINK = '/portal/sign-in';

/** Unread count shown on the bell: hidden at 0, "9+" above 9 (BR-16). */
export function bellCountText(count: number): string | null {
  if (count <= 0) {
    return null;
  }
  return count > 9 ? '9+' : String(count);
}
