export type GlobalSearchEntityType =
  'customer' | 'service_request' | 'quote' | 'work_order' | 'invoice';

export interface GlobalSearchItem {
  readonly entityType: GlobalSearchEntityType;
  readonly entityId: string;
  readonly displayNumber: string | null;
  readonly title: string;
  readonly subtitle: string | null;
  readonly status: string | null;
  readonly branchId: string | null;
  readonly branchName: string | null;
  readonly updatedAt: string;
}

/** `GET /search/global` response. */
export interface GlobalSearchResponse {
  readonly query: string;
  readonly items: readonly GlobalSearchItem[];
  readonly hasMore: boolean;
}
