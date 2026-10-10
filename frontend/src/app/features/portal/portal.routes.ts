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
    title: 'Sign in · Customer portal',
    loadComponent: () => import('./pages/sign-in/sign-in').then((m) => m.PortalSignIn),
  },
  {
    path: 'forgot-password',
    title: 'Reset your password · Customer portal',
    data: { portal: true },
    loadComponent: () =>
      import('../authentication/pages/forgot-password/forgot-password').then(
        (m) => m.ForgotPassword,
      ),
  },
  {
    path: 'reset-password',
    title: 'Create a new password · Customer portal',
    data: { portal: true },
    loadComponent: () =>
      import('../authentication/pages/reset-password/reset-password').then((m) => m.ResetPassword),
  },
  {
    path: 'activate',
    title: 'Activate your access · Customer portal',
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
        title: 'Home · Customer portal',
        loadComponent: () => import('./pages/home/home').then((m) => m.PortalHome),
      },
      {
        path: 'requests',
        pathMatch: 'full',
        title: 'Requests · Customer portal',
        loadComponent: () => import('./pages/requests/requests').then((m) => m.PortalRequests),
      },
      // Before `requests/:requestId` so `new` is never read as an id.
      {
        path: 'requests/new',
        title: 'Request a service · Customer portal',
        loadComponent: () =>
          import('./pages/request-new/request-new').then((m) => m.PortalRequestNew),
      },
      {
        path: 'requests/:requestId',
        title: 'Request · Customer portal',
        loadComponent: () =>
          import('./pages/request-detail/request-detail').then((m) => m.PortalRequestDetail),
      },
      {
        path: 'appointments',
        pathMatch: 'full',
        title: 'Appointments · Customer portal',
        loadComponent: () =>
          import('./pages/appointments/appointments').then((m) => m.PortalAppointments),
      },
      {
        path: 'appointments/:visitId',
        title: 'Appointment · Customer portal',
        loadComponent: () =>
          import('./pages/appointment-detail/appointment-detail').then(
            (m) => m.PortalAppointmentDetail,
          ),
      },
      {
        path: 'quotes',
        pathMatch: 'full',
        title: 'Quotes · Customer portal',
        loadComponent: () => import('./pages/quotes/quotes').then((m) => m.PortalQuotes),
      },
      {
        path: 'quotes/:quoteId',
        title: 'Quote · Customer portal',
        loadComponent: () => import('./pages/portal-quote/portal-quote').then((m) => m.PortalQuote),
      },
      {
        path: 'invoices',
        pathMatch: 'full',
        title: 'Invoices · Customer portal',
        loadComponent: () => import('./pages/invoices/invoices').then((m) => m.PortalInvoices),
      },
      {
        path: 'invoices/:invoiceId',
        title: 'Invoice · Customer portal',
        loadComponent: () =>
          import('./pages/portal-invoice/portal-invoice').then((m) => m.PortalInvoice),
      },
      {
        path: 'properties',
        pathMatch: 'full',
        title: 'Properties · Customer portal',
        loadComponent: () =>
          import('./pages/properties/properties').then((m) => m.PortalProperties),
      },
      {
        path: 'properties/new',
        title: 'Add property · Customer portal',
        loadComponent: () =>
          import('./pages/property-form/property-form').then((m) => m.PortalPropertyForm),
      },
      {
        path: 'properties/:propertyId/edit',
        title: 'Edit property · Customer portal',
        loadComponent: () =>
          import('./pages/property-form/property-form').then((m) => m.PortalPropertyForm),
      },
      {
        path: 'updates',
        title: 'Updates · Customer portal',
        loadComponent: () => import('./pages/updates/updates').then((m) => m.PortalUpdates),
      },
      {
        path: 'activity',
        title: 'Recent activity · Customer portal',
        loadComponent: () => import('./pages/activity/activity').then((m) => m.PortalActivity),
      },
      { path: '**', redirectTo: '' },
    ],
  },
] satisfies Routes;
