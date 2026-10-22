import { Params } from '@angular/router';

import { RecordLinkKind } from '../models/customer.model';

/** Destination of a customer record row (BR-05 to BR-08, BR-11); destinations keep their own authorization. */
export function recordLink(
  kind: RecordLinkKind,
  id: string,
): { readonly path: string; readonly query: Params | null } {
  switch (kind) {
    case 'request':
      return { path: '/requests', query: { request: id } };
    case 'quote':
      return { path: `/quotes/${id}`, query: null };
    case 'job':
      return { path: `/jobs/${id}`, query: null };
    case 'invoice':
      return { path: `/invoices/${id}`, query: null };
  }
}
