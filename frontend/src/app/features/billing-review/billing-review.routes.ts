import { Route } from '@angular/router';

import { withI18n } from '../../core/i18n/i18n-scopes';
/**
 * Completed jobs review (`/invoices/review`); mounted inside the app shell, which owns the auth
 * guard. Not role-guarded: roles without Read get the forbidden state inside the page (BR-22).
 */
export const billingReviewRoute: Route = withI18n(
  {
    path: 'invoices/review',
    pathMatch: 'full',
    title: 'titles.completedJobsReview',
    loadComponent: () =>
      import('./pages/billing-review/billing-review').then((m) => m.BillingReview),
  },
  'billing-review',
);
