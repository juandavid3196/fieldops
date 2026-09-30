import { Route } from '@angular/router';

import { productsServicesUnsavedChangesGuard } from './guards/products-services-unsaved-changes.guard';

/**
 * Products & services (`/admin/products-services`); mounted inside the app shell, which owns the
 * auth guard. Not role-guarded: other roles get the forbidden state inside the shell (BR-02).
 */
export const productsServicesAdminRoute: Route = {
  path: 'admin/products-services',
  pathMatch: 'full',
  title: 'Products & services · FieldOps',
  canDeactivate: [productsServicesUnsavedChangesGuard],
  loadComponent: () =>
    import('./pages/products-services/products-services').then((m) => m.ProductsServices),
};
