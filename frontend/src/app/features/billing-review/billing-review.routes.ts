import { Route } from '@angular/router';

/** `/invoices` lands on the review queue (BR-22). */
export const invoicesRedirectRoute: Route = {
  path: 'invoices',
  pathMatch: 'full',
  redirectTo: 'invoices/review',
};

/**
 * Completed jobs review (`/invoices/review`); mounted inside the app shell, which owns the auth
 * guard. Not role-guarded: roles without Read get the forbidden state inside the page (BR-22).
 */
export const billingReviewRoute: Route = {
  path: 'invoices/review',
  pathMatch: 'full',
  title: 'Completed jobs review · FieldOps',
  loadComponent: () => import('./pages/billing-review/billing-review').then((m) => m.BillingReview),
};
