import { Routes } from '@angular/router';

import { authGuard } from '../../core/guards/auth.guard';
import { companySettingsUnsavedChangesGuard } from './guards/company-settings-unsaved-changes.guard';

export default [
  {
    // Compound path, sibling to 'auth': public registration, no guestGuard.
    path: 'auth/register-company',
    pathMatch: 'full',
    title: 'Create your organization · FieldOps',
    loadComponent: () =>
      import('./pages/register-company/register-company').then((m) => m.RegisterCompany),
  },
  {
    path: 'admin/company',
    pathMatch: 'full',
    title: 'Company setup · FieldOps',
    canActivate: [authGuard],
    canDeactivate: [companySettingsUnsavedChangesGuard],
    loadComponent: () => import('./pages/company-setup/company-setup').then((m) => m.CompanySetup),
  },
] satisfies Routes;
