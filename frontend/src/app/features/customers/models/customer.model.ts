import { INVOICE_READ_ROLES } from '../../invoices/models/invoice.model';
import { MANAGER_ROLES, READ_ROLES } from '../../requests/models/requests.model';

export type CustomerType = 'residential' | 'commercial';
export type CustomerTab = 'all' | 'leads' | 'active' | 'archived';
export type BalanceStatus = 'any' | 'none' | 'has_balance' | 'overdue';
export type CustomerSort = 'last_activity' | 'name_asc' | 'name_desc' | 'newest' | 'balance_desc';
export type Lifecycle = 'lead' | 'active' | 'archived';
export type DisplayStatus = 'archived' | 'overdue' | 'lead' | 'active';
export const PAGE_SIZE = 10;
export const MAX_TAGS = 10;

/** `GET /customers/metrics` (BR-05). */
export interface CustomerMetrics {
  readonly totalCustomers: number;
  readonly activeCustomers: number;
  readonly newThisMonth: number;
  readonly outstandingBalance: number;
  readonly currency: string;
}

export interface CustomerRow {
  readonly id: string;
  readonly type: CustomerType;
  readonly displayName: string;
  readonly primaryEmail: string | null;
  readonly primaryPhone: string | null;
  readonly propertyCount: number;
  readonly lastService: { readonly completedAt: string; readonly summary: string | null } | null;
  readonly nextService: { readonly startsAt: string } | null;
  readonly balance: number;
  readonly lifecycle: Lifecycle;
  readonly displayStatus: DisplayStatus;
  readonly branchId: string;
}

export interface TabCounts {
  readonly all: number;
  readonly leads: number;
  readonly active: number;
  readonly archived: number;
}

export interface CustomerListResponse {
  readonly items: readonly CustomerRow[];
  readonly totalCount: number;
  readonly page: number;
  readonly pageSize: number;
  readonly tabCounts: TabCounts;
  readonly currency: string;
  readonly timezone: string;
}

export interface CustomerFilters {
  readonly tab: CustomerTab;
  readonly search: string;
  readonly type: CustomerType | null;
  readonly branchId: string | null;
  readonly tagIds: readonly string[];
  readonly balanceStatus: BalanceStatus;
  readonly sort: CustomerSort;
}

export interface CustomerListQuery extends CustomerFilters {
  readonly page: number;
}

export interface CustomerTag {
  readonly id: string;
  readonly name: string;
}

/** Portal access of the primary contact (customer-portal-dashboard BR-10). */
export type PortalStatus = 'active' | 'invited' | 'not_invited';

/** Body of `POST /customers/{id}/portal-invitation` and `DELETE /customers/{id}/portal-access`. */
export interface PortalAccess {
  readonly portalStatus: PortalStatus;
  readonly portalLinkedOn: string | null;
  readonly invitationExpiresOn: string | null;
}

export interface CustomerContact extends Partial<PortalAccess> {
  readonly firstName: string;
  readonly lastName: string;
  readonly title?: string | null;
  readonly email: string;
  readonly phone?: string | null;
  readonly prefersEmail: boolean;
  readonly prefersSms: boolean;
}

export interface CustomerProperty {
  readonly addressLine1: string;
  readonly city: string;
  readonly stateRegion?: string | null;
  readonly postalCode?: string | null;
}

/** Body of `GET /customers/{id}` and `PUT` response. */
export interface CustomerDetail {
  readonly id: string;
  readonly type: CustomerType;
  readonly companyName?: string | null;
  readonly contact: CustomerContact;
  readonly property: CustomerProperty;
  readonly serviceInstructions?: string | null;
  readonly internalNote?: string | null;
  readonly branchId: string;
  readonly tags: readonly CustomerTag[];
  readonly lifecycle: Lifecycle;
  readonly displayStatus: DisplayStatus;
  readonly isActive: boolean;
}

/** Body of `POST /customers`; `PUT` omits `type`. */
export interface CustomerRequest {
  readonly type: CustomerType;
  readonly companyName: string | null;
  readonly contact: CustomerContact;
  readonly property: CustomerProperty;
  readonly serviceInstructions: string | null;
  readonly internalNote: string | null;
  readonly branchId: string;
  readonly tagIds: readonly string[];
}

export interface DuplicateCheckRequest {
  readonly email?: string;
  readonly phone?: string;
  readonly excludeCustomerId?: string;
}

export interface DuplicateMatch {
  readonly customerId?: string | null;
  readonly displayName: string;
  readonly primaryEmail?: string | null;
  readonly primaryPhone?: string | null;
  readonly propertyCount?: number | null;
  readonly displayStatus: DisplayStatus;
  readonly matchedField: 'email' | 'phone';
  readonly inScope: boolean;
}

export interface DuplicateCheckResponse {
  readonly matches: readonly DuplicateMatch[];
}

export interface ImportDuplicateWarning {
  readonly row: number;
  readonly matchedField: 'email' | 'phone';
  readonly existingDisplayName: string;
}

export interface ImportRowError {
  readonly row: number;
  readonly column: string;
  readonly message: string;
}

export interface ImportPreview {
  readonly validRowCount: number;
  readonly rowErrors: readonly ImportRowError[];
  readonly duplicateWarnings: readonly ImportDuplicateWarning[];
}

export interface ImportResult {
  readonly importedCount: number;
}

export interface CsvFile {
  readonly blob: Blob;
  readonly fileName: string;
}

export const CUSTOMER_FIELD_KEYS = [
  'type',
  'companyName',
  'firstName',
  'lastName',
  'title',
  'email',
  'phone',
  'preferences',
  'addressLine1',
  'city',
  'stateRegion',
  'postalCode',
  'serviceInstructions',
  'branchId',
  'tagIds',
  'internalNote',
] as const;
export type CustomerFieldKey = (typeof CUSTOMER_FIELD_KEYS)[number];
export type CustomerFieldErrors = Partial<Record<CustomerFieldKey, string>>;

/** `GET /customers/branch-options`: assignable branches and the organization country for State/ZIP rules. */
export interface BranchOptionsResponse {
  readonly countryCode: string;
  readonly branches: readonly { readonly id: string; readonly name: string }[];
}

export type DetailTab = 'overview' | 'requests' | 'quotes' | 'jobs' | 'invoices' | 'activity';
export const DETAIL_TABS: readonly DetailTab[] = [
  'overview',
  'requests',
  'quotes',
  'jobs',
  'invoices',
  'activity',
];
export const NOTES_PAGE_SIZE = 10;

/** Module read policies deciding which transactional tabs a role sees (BR-03). */
export const REQUESTS_READ_ROLES: readonly string[] = [...MANAGER_ROLES, ...READ_ROLES];
export const JOBS_READ_ROLES: readonly string[] = [
  'owner',
  'operations_manager',
  'dispatcher',
  'viewer',
];
export { INVOICE_READ_ROLES };

export type RelatedKind = 'requests' | 'quotes' | 'jobs' | 'invoices';
export const RELATED_KINDS: readonly RelatedKind[] = ['requests', 'quotes', 'jobs', 'invoices'];
export const RELATED_READ_ROLES: Readonly<Record<RelatedKind, readonly string[]>> = {
  requests: REQUESTS_READ_ROLES,
  quotes: REQUESTS_READ_ROLES,
  jobs: JOBS_READ_ROLES,
  invoices: INVOICE_READ_ROLES,
};
export const RELATED_PAGE_SIZES: readonly number[] = [10, 25, 50];

export type InvoiceStatus = 'sent' | 'partially_paid' | 'paid' | 'overdue';

/** `GET /customers/{id}/detail` (FR-02). */
export interface CustomerOverview {
  readonly id: string;
  readonly type: CustomerType;
  readonly displayName: string;
  readonly contact: CustomerContact;
  readonly lifecycle: Lifecycle;
  readonly displayStatus: DisplayStatus;
  readonly isActive: boolean;
  /** Omitted (not zero) for a role without Invoices read (BR-14). */
  readonly outstandingBalance?: number | null;
  readonly currency: string;
  readonly timezone: string;
  readonly summary: {
    /** Omitted without Jobs read. */
    readonly totalJobs?: number | null;
    /** Omitted without Invoices read. */
    readonly lifetimeValue?: number | null;
    readonly customerSince: string;
    readonly lastServiceAt?: string | null;
  };
  readonly lastInvoice: {
    readonly number: string;
    readonly status: InvoiceStatus;
    readonly issueDate?: string | null;
  } | null;
  readonly tags: readonly CustomerTag[];
  readonly pinnedNote?: string | null;
}

/** A property of `GET /customers/{id}/properties[/{propertyId}]`. */
export interface PropertyItem {
  readonly id: string;
  readonly name: string;
  readonly isPrimary: boolean;
  readonly isActive: boolean;
  readonly addressLine1: string;
  readonly addressLine2?: string | null;
  readonly city: string;
  readonly stateRegion?: string | null;
  readonly postalCode?: string | null;
  readonly branch: { readonly id: string; readonly name: string } | null;
  readonly serviceInstructions?: string | null;
  readonly lastService: { readonly completedAt: string; readonly summary: string | null } | null;
  readonly nextAppointment: { readonly startsAt: string } | null;
}

export interface PropertiesResponse {
  readonly items: readonly PropertyItem[];
  readonly timezone: string;
}

/** Body of `POST` and `PUT /customers/{id}/properties[/{propertyId}]`. */
export interface PropertyRequest {
  readonly name: string;
  readonly addressLine1: string;
  readonly addressLine2: string | null;
  readonly city: string;
  readonly stateRegion: string | null;
  readonly postalCode: string | null;
  readonly branchId: string;
  readonly serviceInstructions: string | null;
}

export type RecentWorkType = 'request' | 'quote' | 'job';

export interface RecentWorkItem {
  readonly type: RecentWorkType;
  readonly id: string;
  readonly number: string;
  readonly title: string;
  readonly status: string;
  readonly date: string;
  readonly technicianName?: string | null;
  readonly amount?: number | null;
}

export interface RecentWorkResponse {
  readonly items: readonly RecentWorkItem[];
  readonly currency: string;
  readonly timezone: string;
}

export interface AppointmentItem {
  readonly visitId: string;
  readonly startsAt: string;
  readonly endsAt?: string | null;
  readonly jobNumber: string;
  readonly jobTitle: string;
  readonly propertyName: string;
  readonly technicianName?: string | null;
}

export interface AppointmentsResponse {
  readonly items: readonly AppointmentItem[];
  readonly timezone: string;
}

export interface NoteItem {
  readonly id: string;
  readonly note: string;
  readonly authorName: string;
  readonly createdAt: string;
}

export interface NotesResponse {
  readonly items: readonly NoteItem[];
  readonly totalCount: number;
  readonly page: number;
  readonly pageSize: number;
  readonly timezone: string;
}

export type ActivityType =
  'customer' | 'requests' | 'messages' | 'quotes' | 'jobs' | 'visits' | 'invoices' | 'payments';
export type ActivityPeriod =
  'last_7_days' | 'last_30_days' | 'last_90_days' | 'last_12_months' | 'all';
export const ACTIVITY_PERIODS: readonly ActivityPeriod[] = [
  'last_7_days',
  'last_30_days',
  'last_90_days',
  'last_12_months',
  'all',
];
export type RecordLinkKind = 'request' | 'quote' | 'job' | 'invoice';

export interface ActivityItem {
  readonly id: string;
  readonly type: ActivityType;
  readonly action: string;
  readonly occurredAt: string;
  readonly origin: {
    readonly kind: 'customer_portal' | 'staff' | 'system';
    readonly actorName?: string | null;
  };
  readonly subject: {
    readonly number?: string | null;
    readonly title?: string | null;
    readonly service?: string | null;
    readonly amount?: number | null;
    readonly invoiceNumbers?: readonly string[] | null;
    readonly contactName?: string | null;
    readonly propertyName?: string | null;
    readonly scheduledAt?: string | null;
    readonly visitNumber?: number | string | null;
    readonly newStatus?: string | null;
  };
  readonly link?: { readonly kind: RecordLinkKind; readonly id: string } | null;
}

/** `GET /customers/{id}/activity` (BR-12): one cursor block of 20 events. */
export interface ActivityResponse {
  readonly items: readonly ActivityItem[];
  readonly availableTypes: readonly ActivityType[];
  readonly nextCursor?: string | null;
  readonly currency: string;
  readonly timezone: string;
}

export interface ActivityQuery {
  readonly type: ActivityType | 'all';
  readonly period: ActivityPeriod;
  readonly before?: string | null;
}

/** Query of the four related lists (BR-04). `status` is a code of the tab's status set. */
export interface RelatedQuery {
  readonly search: string;
  readonly status: string | null;
  readonly page: number;
  readonly pageSize: number;
}

interface RelatedPage<T> {
  readonly items: readonly T[];
  readonly totalCount: number;
  readonly customerTotal: number;
  readonly page: number;
  readonly pageSize: number;
  readonly timezone: string;
}

export interface RelatedRequestItem {
  readonly id: string;
  readonly number: string;
  readonly service: string;
  readonly propertyName?: string | null;
  readonly createdAt: string;
  readonly status: string;
}

export interface RelatedQuoteItem {
  readonly id: string;
  readonly number: string;
  readonly request: { readonly id: string; readonly number: string; readonly service: string };
  readonly total?: number | null;
  readonly validUntil?: string | null;
  readonly status: string;
}

export interface RelatedJobItem {
  readonly id: string;
  readonly number: string;
  readonly title: string;
  readonly propertyName?: string | null;
  readonly nextVisitAt?: string | null;
  readonly completedAt?: string | null;
  readonly technician?: { readonly name: string; readonly initials: string } | null;
  readonly status: string;
}

export interface RelatedInvoiceItem {
  readonly id: string;
  readonly number: string;
  readonly job: { readonly id: string; readonly number: string };
  readonly issueDate?: string | null;
  readonly dueDate?: string | null;
  readonly balanceDue: number;
  readonly status: string;
}

export interface RelatedInvoicesSummary {
  readonly outstanding: number;
  readonly overdue: number;
  readonly paid: number;
}

export interface RelatedResponses {
  readonly requests: RelatedPage<RelatedRequestItem>;
  readonly quotes: RelatedPage<RelatedQuoteItem> & { readonly currency: string };
  readonly jobs: RelatedPage<RelatedJobItem>;
  readonly invoices: RelatedPage<RelatedInvoiceItem> & {
    readonly currency: string;
    readonly summary: RelatedInvoicesSummary;
  };
}
export type RelatedResponse = RelatedResponses[RelatedKind];
export type RelatedItem = RelatedResponse['items'][number];

/** Load state of one detail-page region (header, properties, recent work, ...). */
export interface RegionState<T> {
  readonly status: 'idle' | 'loading' | 'ready' | 'error';
  readonly data: T | null;
}

export const IDLE_REGION: RegionState<never> = { status: 'idle', data: null };
