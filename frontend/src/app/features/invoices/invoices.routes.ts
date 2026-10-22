import { Route } from '@angular/router';

import { invoiceUnsavedChangesGuard } from './guards/invoice-unsaved-changes.guard';

import { withI18n } from '../../core/i18n/i18n-scopes';
/**
 * Invoices and payments hub (`/invoices`, BR-20): mounted inside the app shell, which owns the
 * auth guard. Not role-guarded: roles without Read get the forbidden state inside the page.
 */
export const invoicesHubRoute: Route = withI18n(
  {
    path: 'invoices',
    pathMatch: 'full',
    title: 'titles.invoicesAndPayments',
    loadComponent: () => import('./pages/invoices-hub/invoices-hub').then((m) => m.InvoicesHub),
  },
  'invoices',
);

/**
 * Invoice page (`/invoices/:invoiceId`, BR-23); mounted inside the app shell, which owns the auth
 * guard. Not role-guarded: roles without Read get the forbidden state inside the page. Declare it
 * after `invoices/review` so the review queue keeps precedence.
 */
export const invoiceDetailRoute: Route = withI18n(
  {
    path: 'invoices/:invoiceId',
    pathMatch: 'full',
    title: 'titles.invoice',
    canDeactivate: [invoiceUnsavedChangesGuard],
    loadComponent: () =>
      import('./pages/invoice-detail/invoice-detail').then((m) => m.InvoiceDetailPage),
  },
  'invoices',
);

/**
 * Public invoice link (`/invoices/view`, BR-28): the emailed link carries the token in the URL
 * fragment. Declared outside the authenticated shell and before the shell so it is never matched
 * as an invoice id.
 */
export const publicInvoiceRoute: Route = withI18n(
  {
    path: 'invoices/view',
    pathMatch: 'full',
    title: 'titles.invoice',
    loadComponent: () =>
      import('./pages/public-invoice/public-invoice').then((m) => m.PublicInvoicePage),
  },
  'invoices',
);
