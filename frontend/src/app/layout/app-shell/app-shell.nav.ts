import { comingSoonPath } from '../../core/config/coming-soon-modules';

/** One sidebar link. `exact` links match only their own URL (`aria-current="page"`). */
export interface NavItem {
  readonly label: string;
  readonly icon: string;
  readonly link: string;
  readonly exact: boolean;
  /** Non-exact items are current under this path prefix instead of their own link. */
  readonly activePrefix?: string;
}

/** A sidebar block: an optional heading label (never a link) and its items. */
export interface NavGroup {
  readonly id: string;
  readonly heading: string | null;
  readonly items: readonly NavItem[];
  /** Rendered after a divider. */
  readonly divider?: boolean;
  /** BR-01: shown only to `owner` and `viewer` (UX only; the backend decides). */
  readonly adminOnly?: boolean;
}

function moduleItem(label: string, icon: string, slug: string): NavItem {
  return { label, icon, link: comingSoonPath(slug), exact: false };
}

/** BR-01: sidebar structure, order and icons. */
export const NAV_GROUPS: readonly NavGroup[] = [
  {
    id: 'overview',
    heading: null,
    items: [{ label: 'Overview', icon: 'pi-th-large', link: '/overview', exact: true }],
  },
  {
    id: 'operations',
    heading: 'Operations',
    items: [
      moduleItem('Requests', 'pi-inbox', 'requests'),
      moduleItem('Quotes', 'pi-file-edit', 'quotes'),
      moduleItem('Work orders', 'pi-wrench', 'work-orders'),
      moduleItem('Schedule', 'pi-calendar', 'schedule'),
    ],
  },
  {
    id: 'people',
    heading: 'People',
    items: [
      { label: 'Customers', icon: 'pi-users', link: '/customers', exact: false },
      moduleItem('Team', 'pi-id-card', 'team'),
    ],
  },
  {
    id: 'finance',
    heading: 'Finance',
    items: [
      {
        label: 'Products and services',
        icon: 'pi-box',
        link: '/admin/products-services',
        exact: true,
      },
      moduleItem('Invoices', 'pi-receipt', 'invoices'),
      moduleItem('Reports', 'pi-chart-bar', 'reports'),
    ],
  },
  {
    id: 'administration',
    heading: null,
    divider: true,
    adminOnly: true,
    items: [
      {
        label: 'Administration',
        icon: 'pi-cog',
        link: '/admin/company',
        exact: false,
        activePrefix: '/admin',
      },
    ],
  },
];

/** BR-01: role codes allowed to see Administration. */
export const ADMINISTRATION_ROLE_CODES: readonly string[] = ['owner', 'viewer'];
