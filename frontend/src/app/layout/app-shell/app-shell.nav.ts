import { localizedList, t } from '../../core/i18n/translate';
/** One sidebar link. `exact` links match only their own URL (`aria-current="page"`). */
export interface NavItem {
  readonly label: string;
  readonly icon: string;
  readonly link: string;
  readonly exact: boolean;
  /** Non-exact items are current under this path prefix instead of their own link. */
  readonly activePrefix?: string;
  /**
   * Query params of the link. An item that defines them (an empty object for the default view)
   * is current only when its path equals the link and the query matches them exactly.
   */
  readonly queryParams?: Readonly<Record<string, string>>;
  /** audit-trail BR-23: shown only to these role codes (UX only; the backend decides). */
  readonly roles?: readonly string[];
  /** technician-messages BR-15: shows the unread messages badge. */
  readonly unreadMessages?: boolean;
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
  /** notifications-center BR-17: hidden for `technician` (UX only; the backend decides). */
  readonly internalOnly?: boolean;
  /** BR-16: shown only to `technician` (UX only; the backend decides). */
  readonly technicianOnly?: boolean;
  /** operations-dashboard BR-01: shown only to the Manage roles (UX only). */
  readonly manageOnly?: boolean;
  /** operations-dashboard BR-01: hidden for the Manage roles, who get the Dashboard instead. */
  readonly hiddenForManage?: boolean;
}

/** audit-trail BR-23: roles that see the Audit log item. */
export const AUDIT_LOG_ROLE_CODES: readonly string[] = [
  'owner',
  'operations_manager',
  'accounting',
];

/** reports-workspace BR-23: roles that see the Reports item (UX only; the backend decides). */
export const REPORTS_ROLE_CODES: readonly string[] = [
  'owner',
  'operations_manager',
  'dispatcher',
  'accounting',
];

/** technician-messages BR-15: roles that see the Messages item (UX only; the backend decides). */
export const MESSAGES_ROLE_CODES: readonly string[] = ['owner', 'operations_manager', 'dispatcher'];

/**
 * Roles that can open each page, mirroring the pages' own read rules so the sidebar never offers a
 * page that would only show its "no access" state (UX only; the backend decides).
 */
/** Requests, Quotes, Customers, Products and services, Invoices. */
export const INTERNAL_ROLE_CODES: readonly string[] = [
  'owner',
  'operations_manager',
  'dispatcher',
  'accounting',
  'viewer',
];
/** Jobs and Schedule. */
export const WORK_ROLE_CODES: readonly string[] = [
  'owner',
  'operations_manager',
  'dispatcher',
  'viewer',
];
/** Team (a technician sees their own profile). */
export const TEAM_ROLE_CODES: readonly string[] = [
  'owner',
  'operations_manager',
  'dispatcher',
  'technician',
];

/** BR-01: sidebar structure, order and icons. */
export const NAV_GROUPS: readonly NavGroup[] = localizedList((): readonly NavGroup[] => [
  {
    id: 'dashboard',
    heading: null,
    manageOnly: true,
    items: [
      {
        label: t('shell.appShellNav.dashboard'),
        icon: 'pi-th-large',
        link: '/dashboard',
        exact: true,
      },
    ],
  },
  {
    id: 'overview',
    heading: null,
    hiddenForManage: true,
    items: [
      {
        label: t('shell.appShellNav.overview'),
        icon: 'pi-th-large',
        link: '/overview',
        exact: true,
      },
    ],
  },
  {
    id: 'technician',
    heading: null,
    technicianOnly: true,
    items: [
      {
        label: t('shell.appShellNav.todaysJobs'),
        icon: 'pi-briefcase',
        link: '/today',
        exact: true,
      },
    ],
  },
  {
    id: 'operations',
    heading: t('shell.appShellNav.operations'),
    items: [
      {
        label: t('shell.appShellNav.requests'),
        icon: 'pi-inbox',
        link: '/requests',
        exact: false,
        roles: INTERNAL_ROLE_CODES,
      },
      {
        label: t('shell.appShellNav.quotes'),
        icon: 'pi-file-edit',
        link: '/quotes',
        exact: false,
        roles: INTERNAL_ROLE_CODES,
      },
      {
        label: t('shell.appShellNav.jobs'),
        icon: 'pi-wrench',
        link: '/jobs',
        exact: false,
        roles: WORK_ROLE_CODES,
      },
      {
        label: t('shell.appShellNav.schedule'),
        icon: 'pi-calendar',
        link: '/schedule',
        exact: false,
        roles: WORK_ROLE_CODES,
      },
      {
        label: t('shell.appShellNav.messages'),
        icon: 'pi-comments',
        link: '/messages',
        exact: false,
        roles: MESSAGES_ROLE_CODES,
        unreadMessages: true,
      },
    ],
  },
  {
    id: 'people',
    heading: t('shell.appShellNav.people'),
    items: [
      {
        label: t('shell.appShellNav.customers'),
        icon: 'pi-users',
        link: '/customers',
        exact: false,
        roles: INTERNAL_ROLE_CODES,
      },
      {
        label: t('shell.appShellNav.team'),
        icon: 'pi-id-card',
        link: '/team',
        exact: false,
        roles: TEAM_ROLE_CODES,
      },
    ],
  },
  {
    id: 'finance',
    heading: t('shell.appShellNav.finance'),
    items: [
      {
        label: t('shell.appShellNav.productsAndServices'),
        icon: 'pi-box',
        link: '/admin/products-services',
        exact: true,
        roles: INTERNAL_ROLE_CODES,
      },
      {
        label: t('shell.appShellNav.invoices'),
        icon: 'pi-receipt',
        link: '/invoices',
        exact: false,
        activePrefix: '/invoices',
        roles: INTERNAL_ROLE_CODES,
        children: [
          {
            label: t('shell.appShellNav.allInvoices'),
            icon: 'pi-receipt',
            link: '/invoices',
            exact: false,
            queryParams: {},
          },
          {
            label: t('shell.appShellNav.needsReview'),
            icon: 'pi-list-check',
            link: '/invoices/review',
            exact: false,
          },
          {
            label: t('shell.appShellNav.drafts'),
            icon: 'pi-file-edit',
            link: '/invoices',
            exact: false,
            queryParams: { status: 'draft' },
          },
          {
            label: t('shell.appShellNav.sent'),
            icon: 'pi-send',
            link: '/invoices',
            exact: false,
            queryParams: { status: 'sent' },
          },
          {
            label: t('shell.appShellNav.payments'),
            icon: 'pi-wallet',
            link: '/invoices',
            exact: false,
            queryParams: { tab: 'payments' },
          },
          {
            label: t('shell.appShellNav.overdue'),
            icon: 'pi-clock',
            link: '/invoices',
            exact: false,
            queryParams: { status: 'overdue' },
          },
        ],
      },
      {
        label: t('shell.appShellNav.reports'),
        icon: 'pi-chart-bar',
        link: '/reports',
        exact: false,
        roles: REPORTS_ROLE_CODES,
      },
      {
        label: t('shell.appShellNav.auditLog'),
        icon: 'pi-history',
        link: '/audit-log',
        exact: false,
        roles: AUDIT_LOG_ROLE_CODES,
      },
    ],
  },
  {
    id: 'notifications',
    heading: null,
    divider: true,
    internalOnly: true,
    items: [
      {
        label: t('shell.appShellNav.notifications'),
        icon: 'pi-bell',
        link: '/notifications',
        exact: false,
      },
    ],
  },
  {
    id: 'administration',
    heading: null,
    divider: true,
    adminOnly: true,
    items: [
      {
        label: t('shell.appShellNav.administration'),
        icon: 'pi-cog',
        link: '/admin/company',
        exact: false,
        activePrefix: '/admin',
      },
    ],
  },
]);

/** BR-01: role codes allowed to see Administration. */
export const ADMINISTRATION_ROLE_CODES: readonly string[] = ['owner', 'viewer'];
