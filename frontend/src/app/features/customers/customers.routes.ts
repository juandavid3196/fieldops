import { Route } from '@angular/router';

import { customerDetailUnsavedChangesGuard } from './guards/customer-detail-unsaved-changes.guard';
import { customersUnsavedChangesGuard } from './guards/customers-unsaved-changes.guard';

/**
 * Customers (`/customers`); mounted inside the app shell, which owns the auth guard. Not
 * role-guarded: technicians and unknown roles get the forbidden state inside the shell (BR-18).
 */
export const customersRoute: Route = {
  path: 'customers',
  pathMatch: 'full',
  title: 'Customers · FieldOps',
  canDeactivate: [customersUnsavedChangesGuard],
  loadComponent: () => import('./pages/customers/customers').then((m) => m.Customers),
};

/** Customer detail (`/customers/:customerId`); same shell and role handling as the list (BR-01). */
export const customerDetailRoute: Route = {
  path: 'customers/:customerId',
  pathMatch: 'full',
  title: 'Customer · FieldOps',
  canDeactivate: [customerDetailUnsavedChangesGuard],
  loadComponent: () =>
    import('./pages/customer-detail/customer-detail').then((m) => m.CustomerDetail),
};
