import { comingSoonPath } from '../../core/config/coming-soon-modules';

/** One sidebar link. `exact` links match only their own URL (`aria-current="page"`). */
export interface NavItem {
  readonly label: string;
  readonly icon: string;
  readonly link: string;
  readonly exact: boolean;
  /** Non-exact items are current under this path prefix instead of their own link. */
  readonly activePrefix?: string;
  /** Sub-items rendered as an expandable group under this item (sidebar only). */
  readonly children?: readonly NavItem[];
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
  /** BR-16: shown only to `technician` (UX only; the backend decides). */
  readonly technicianOnly?: boolean;
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
    id: 'technician',
    heading: null,
    technicianOnly: true,
    items: [{ label: "Today's jobs", icon: 'pi-briefcase', link: '/today', exact: true }],
  },
  {
    id: 'operations',
    heading: 'Operations',
    items: [
      { label: 'Requests', icon: 'pi-inbox', link: '/requests', exact: false },
      moduleItem('Quotes', 'pi-file-edit', 'quotes'),
      { label: 'Jobs', icon: 'pi-wrench', link: '/jobs', exact: false },
      { label: 'Schedule', icon: 'pi-calendar', link: '/schedule', exact: false },
    ],
  },
  {
    id: 'people',
    heading: 'People',
    items: [
      { label: 'Customers', icon: 'pi-users', link: '/customers', exact: false },
      { label: 'Team', icon: 'pi-id-card', link: '/team', exact: false },
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
      {
        label: 'Invoices',
        icon: 'pi-receipt',
        link: '/invoices/review',
        exact: false,
        activePrefix: '/invoices',
        children: [
          { label: 'Needs review', icon: 'pi-list-check', link: '/invoices/review', exact: false },
          moduleItem('Drafts', 'pi-file-edit', 'invoice-drafts'),
          moduleItem('Sent', 'pi-send', 'invoice-sent'),
          moduleItem('Payments', 'pi-wallet', 'invoice-payments'),
          moduleItem('Overdue', 'pi-clock', 'invoice-overdue'),
        ],
      },
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
