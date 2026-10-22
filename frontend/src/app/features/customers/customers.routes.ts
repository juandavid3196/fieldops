import { Route } from '@angular/router';

import { customerDetailUnsavedChangesGuard } from './guards/customer-detail-unsaved-changes.guard';
import { customersUnsavedChangesGuard } from './guards/customers-unsaved-changes.guard';

import { withI18n } from '../../core/i18n/i18n-scopes';
/**
 * Customers (`/customers`); mounted inside the app shell, which owns the auth guard. Not
 * role-guarded: technicians and unknown roles get the forbidden state inside the shell (BR-18).
 */
export const customersRoute: Route = withI18n(
  {
    path: 'customers',
    pathMatch: 'full',
    title: 'titles.customers',
    canDeactivate: [customersUnsavedChangesGuard],
    loadComponent: () => import('./pages/customers/customers').then((m) => m.Customers),
  },
  'customers',
);

/** Customer detail (`/customers/:customerId`); same shell and role handling as the list (BR-01). */
export const customerDetailRoute: Route = withI18n(
  {
    path: 'customers/:customerId',
    pathMatch: 'full',
    title: 'titles.customer',
    canDeactivate: [customerDetailUnsavedChangesGuard],
    loadComponent: () =>
      import('./pages/customer-detail/customer-detail').then((m) => m.CustomerDetail),
  },
  'customers',
);
