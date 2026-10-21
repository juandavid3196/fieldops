import { Routes } from '@angular/router';

import { portalGuard } from '../../core/guards/portal.guard';

/**
 * Customer portal (`/portal/...`). Sign-in, recovery and activation are public on purpose and
 * independent of the internal session; every other page sits inside the portal shell, whose guard
 * revalidates the portal session on every navigation (UX only: the backend authorizes).
 */
export default [
  {
    path: 'sign-in',
    title: 'titles.portalSignIn',
    loadComponent: () => import('./pages/sign-in/sign-in').then((m) => m.PortalSignIn),
  },
  {
    path: 'forgot-password',
    title: 'titles.portalResetYourPassword',
    data: { portal: true },
    loadComponent: () =>
      import('../authentication/pages/forgot-password/forgot-password').then(
        (m) => m.ForgotPassword,
      ),
  },
  {
    path: 'reset-password',
    title: 'titles.portalCreateANewPassword',
    data: { portal: true },
    loadComponent: () =>
      import('../authentication/pages/reset-password/reset-password').then((m) => m.ResetPassword),
  },
  {
    path: 'activate',
    title: 'titles.portalActivateYourAccess',
    loadComponent: () => import('./pages/activate/activate').then((m) => m.PortalActivate),
  },
  {
    path: '',
    canActivate: [portalGuard],
    runGuardsAndResolvers: 'always',
    loadComponent: () =>
      import('../../layout/portal-shell/portal-shell').then((m) => m.PortalShell),
    children: [
      {
        path: '',
        pathMatch: 'full',
        title: 'titles.portalHome',
        loadComponent: () => import('./pages/home/home').then((m) => m.PortalHome),
      },
      {
        path: 'requests',
        pathMatch: 'full',
        title: 'titles.portalRequests',
        loadComponent: () => import('./pages/requests/requests').then((m) => m.PortalRequests),
      },
      // Before `requests/:requestId` so `new` is never read as an id.
      {
        path: 'requests/new',
        title: 'titles.portalRequestAService',
        loadComponent: () =>
          import('./pages/request-new/request-new').then((m) => m.PortalRequestNew),
      },
      {
        path: 'requests/:requestId',
        title: 'titles.portalRequest',
        loadComponent: () =>
          import('./pages/request-detail/request-detail').then((m) => m.PortalRequestDetail),
      },
      {
        path: 'appointments',
        pathMatch: 'full',
        title: 'titles.portalAppointments',
        loadComponent: () =>
          import('./pages/appointments/appointments').then((m) => m.PortalAppointments),
      },
      {
        path: 'appointments/:visitId',
        title: 'titles.portalAppointment',
        loadComponent: () =>
          import('./pages/appointment-detail/appointment-detail').then(
            (m) => m.PortalAppointmentDetail,
          ),
      },
      {
        path: 'quotes',
        pathMatch: 'full',
        title: 'titles.portalQuotes',
        loadComponent: () => import('./pages/quotes/quotes').then((m) => m.PortalQuotes),
      },
      {
        path: 'quotes/:quoteId',
        title: 'titles.portalQuote',
        loadComponent: () => import('./pages/portal-quote/portal-quote').then((m) => m.PortalQuote),
      },
      {
        path: 'invoices',
        pathMatch: 'full',
        title: 'titles.portalInvoices',
        loadComponent: () => import('./pages/invoices/invoices').then((m) => m.PortalInvoices),
      },
      {
        path: 'invoices/:invoiceId',
        title: 'titles.portalInvoice',
        loadComponent: () =>
          import('./pages/portal-invoice/portal-invoice').then((m) => m.PortalInvoice),
      },
      {
        path: 'properties',
        pathMatch: 'full',
        title: 'titles.portalProperties',
        loadComponent: () =>
          import('./pages/properties/properties').then((m) => m.PortalProperties),
      },
      {
        path: 'properties/new',
        title: 'titles.portalAddProperty',
        loadComponent: () =>
          import('./pages/property-form/property-form').then((m) => m.PortalPropertyForm),
      },
      {
        path: 'properties/:propertyId/edit',
        title: 'titles.portalEditProperty',
        loadComponent: () =>
          import('./pages/property-form/property-form').then((m) => m.PortalPropertyForm),
      },
      {
        path: 'updates',
        title: 'titles.portalUpdates',
        loadComponent: () => import('./pages/updates/updates').then((m) => m.PortalUpdates),
      },
      {
        path: 'activity',
        title: 'titles.portalRecentActivity',
        loadComponent: () => import('./pages/activity/activity').then((m) => m.PortalActivity),
      },
      // help-support-center FR-16: declared before the catch-all; the portal guard above applies.
      { path: 'help', loadChildren: () => import('../help/portal-help.routes') },
      { path: '**', redirectTo: '' },
    ],
  },
] satisfies Routes;
