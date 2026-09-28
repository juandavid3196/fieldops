import { Route, Routes } from '@angular/router';

import { companySettingsUnsavedChangesGuard } from './guards/company-settings-unsaved-changes.guard';

/** Authenticated company settings; mounted inside the app shell, which owns the auth guard. */
export const companyAdminRoute: Route = {
  path: 'admin/company',
  pathMatch: 'full',
  title: 'Company setup · FieldOps',
  canDeactivate: [companySettingsUnsavedChangesGuard],
  loadComponent: () => import('./pages/company-setup/company-setup').then((m) => m.CompanySetup),
};

export default [
  {
    // Compound path, sibling to 'auth': public registration, no guestGuard.
    path: 'auth/register-company',
    pathMatch: 'full',
    title: 'Create your organization · FieldOps',
    loadComponent: () =>
      import('./pages/register-company/register-company').then((m) => m.RegisterCompany),
  },
] satisfies Routes;
