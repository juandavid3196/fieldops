import { Route } from '@angular/router';

import { invoiceUnsavedChangesGuard } from './guards/invoice-unsaved-changes.guard';

/**
 * Invoices and payments hub (`/invoices`, BR-20): mounted inside the app shell, which owns the
 * auth guard. Not role-guarded: roles without Read get the forbidden state inside the page.
 */
export const invoicesHubRoute: Route = {
  path: 'invoices',
  pathMatch: 'full',
  title: 'Invoices & payments · FieldOps',
  loadComponent: () => import('./pages/invoices-hub/invoices-hub').then((m) => m.InvoicesHub),
};

/**
 * Invoice page (`/invoices/:invoiceId`, BR-23); mounted inside the app shell, which owns the auth
 * guard. Not role-guarded: roles without Read get the forbidden state inside the page. Declare it
 * after `invoices/review` so the review queue keeps precedence.
 */
export const invoiceDetailRoute: Route = {
  path: 'invoices/:invoiceId',
  pathMatch: 'full',
  title: 'Invoice · FieldOps',
  canDeactivate: [invoiceUnsavedChangesGuard],
  loadComponent: () =>
    import('./pages/invoice-detail/invoice-detail').then((m) => m.InvoiceDetailPage),
};

/**
 * Public invoice link (`/invoices/view`, BR-28): the emailed link carries the token in the URL
 * fragment. Declared outside the authenticated shell and before the shell so it is never matched
 * as an invoice id.
 */
export const publicInvoiceRoute: Route = {
  path: 'invoices/view',
  pathMatch: 'full',
  title: 'Invoice · FieldOps',
  loadComponent: () =>
    import('./pages/public-invoice/public-invoice').then((m) => m.PublicInvoicePage),
};
