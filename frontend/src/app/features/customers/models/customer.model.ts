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

export interface CustomerContact {
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
