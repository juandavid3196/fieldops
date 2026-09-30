export type CatalogType = 'service' | 'product';
export type TaxStatusFilter = 'taxable' | 'non_taxable';
export type StatusFilter = 'active' | 'inactive';
export type CatalogSortKey = 'name' | 'type' | 'unitCost' | 'unitPrice' | 'taxable' | 'status';
/** BR-03 `sort` value: a key, prefixed with `-` for descending. */
export type CatalogSort = CatalogSortKey | `-${CatalogSortKey}`;
export const PAGE_SIZE = 10;

/** Row of `GET /catalog-items` (BR-04). */
export interface CatalogRow {
  readonly id: string;
  readonly type: CatalogType;
  readonly name: string;
  readonly description: string | null;
  readonly unitCost: number;
  readonly unitPrice: number;
  readonly estimatedMarginPercent: number | null;
  readonly isTaxable: boolean;
  readonly isActive: boolean;
  readonly hasImage: boolean;
  readonly updatedAt: string;
}

export interface CatalogImage {
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly updatedAt: string;
}

export interface CatalogUsage {
  readonly quotes: number;
  readonly jobs: number;
  readonly invoices: number;
}

/** Body of `GET /catalog-items/{id}` and every item write response. */
export interface CatalogDetail extends CatalogRow {
  readonly image: CatalogImage | null;
  readonly usage: CatalogUsage;
}

export interface CatalogListResponse {
  readonly items: readonly CatalogRow[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

export interface CatalogFilters {
  readonly type: CatalogType | null;
  readonly search: string;
  readonly taxStatus: TaxStatusFilter | null;
  readonly status: StatusFilter | null;
  readonly sort: CatalogSort;
}

export interface CatalogListQuery extends CatalogFilters {
  readonly page: number;
}

/** `GET /catalog-items/summary` (BR-05). */
export interface CatalogSummary {
  readonly activeItems: number;
  readonly activeServices: number;
  readonly activeProducts: number;
  readonly inactiveItems: number;
  readonly allItems: number;
  readonly services: number;
  readonly products: number;
  readonly currency: string;
}

/** Body of `POST`/`PUT /catalog-items` (BR-07). */
export interface CatalogItemRequest {
  readonly type: CatalogType;
  readonly name: string;
  readonly description: string | null;
  readonly unitCost: number;
  readonly unitPrice: number;
  readonly isTaxable: boolean;
  readonly isActive: boolean;
}

export interface ImportResult {
  readonly importedCount: number;
}

/** A downloaded CSV: the server's file name (when exposed) or the client fallback. */
export interface CsvFile {
  readonly blob: Blob;
  readonly fileName: string;
}

export const ITEM_FIELD_KEYS = ['type', 'name', 'description', 'unitCost', 'unitPrice'] as const;
export type ItemFieldKey = (typeof ITEM_FIELD_KEYS)[number];
export type ItemFieldErrors = Partial<Record<ItemFieldKey, string>>;
