import { Route, Routes } from '@angular/router';

import { companySettingsUnsavedChangesGuard } from './guards/company-settings-unsaved-changes.guard';
import { businessHoursUnsavedChangesGuard } from './guards/business-hours-unsaved-changes.guard';
import { registerCompanyLeaveGuard } from './guards/register-company-leave.guard';

import { withI18n } from '../../core/i18n/i18n-scopes';
/** Authenticated company settings; mounted inside the app shell, which owns the auth guard. */
export const companyAdminRoute: Route = withI18n(
  {
    path: 'admin/company',
    pathMatch: 'full',
    title: 'titles.companySetup',
    canDeactivate: [companySettingsUnsavedChangesGuard],
    loadComponent: () => import('./pages/company-setup/company-setup').then((m) => m.CompanySetup),
  },
  'organizations',
);

/** Branch business hours (`/admin/business-hours`); mounted inside the app shell. */
export const businessHoursAdminRoute: Route = withI18n(
  {
    path: 'admin/business-hours',
    pathMatch: 'full',
    title: 'titles.businessHours',
    canDeactivate: [businessHoursUnsavedChangesGuard],
    loadComponent: () =>
      import('./pages/business-hours/business-hours').then((m) => m.BusinessHours),
  },
  'organizations',
);

export default [
  {
    // Compound path, sibling to 'auth': public registration, no guestGuard.
    path: 'auth/register-company',
    pathMatch: 'full',
    title: 'titles.createYourOrganization',
    canDeactivate: [registerCompanyLeaveGuard],
    loadComponent: () =>
      import('./pages/register-company/register-company').then((m) => m.RegisterCompany),
  },
] satisfies Routes;
