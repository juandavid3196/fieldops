import { Routes } from '@angular/router';

import { authGuard } from './core/guards/auth.guard';
import { withI18n } from './core/i18n/i18n-scopes';
import { auditLogRoute } from './features/audit-log/audit-log.routes';
import { billingReviewRoute } from './features/billing-review/billing-review.routes';
import { comingSoonRoute } from './features/coming-soon/coming-soon.routes';
import { dashboardRoute } from './features/dashboard/dashboard.routes';
import { customerDetailRoute, customersRoute } from './features/customers/customers.routes';
import {
  invoiceDetailRoute,
  invoicesHubRoute,
  publicInvoiceRoute,
} from './features/invoices/invoices.routes';
import { jobDetailRoute, jobsRoute } from './features/jobs/jobs.routes';
import { staffMessagesRoute } from './features/messages/messages.routes';
import { notificationsRoute } from './features/notifications/notifications.routes';
import {
  quoteEditRoute,
  quoteViewRoute,
  quotesRoute,
  workOrderEditorRoute,
} from './features/quotes/quotes.routes';
import {
  businessHoursAdminRoute,
  companyAdminRoute,
} from './features/organizations/organizations.routes';
import { productsServicesAdminRoute } from './features/products-services/products-services.routes';
import { assessmentRoute, requestsRoute } from './features/requests/requests.routes';
import { reportsRoute } from './features/reports/reports.routes';
import { TECHNICIAN_SHELL_ROUTES } from './features/technician-jobs/technician-jobs.routes';
import {
  skillsAvailabilityRoute,
  teamRoute,
  technicianJobHistoryRoute,
  technicianOverviewRoute,
  technicianProfileRedirectRoute,
} from './features/team/team.routes';
import { scheduleRoute } from './features/schedule/schedule.routes';
import { usersAdminRoute } from './features/users/users.routes';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'auth/sign-in' },
  withI18n(
    {
      path: 'auth',
      loadChildren: () => import('./features/authentication/authentication.routes'),
    },
    'authentication',
  ),
  withI18n(
    {
      // Public on purpose: anonymous request form, outside the authenticated shell.
      path: 'request/:slug',
      loadChildren: () => import('./features/service-request/service-request.routes'),
    },
    'service-request',
  ),
  withI18n(
    {
      // Public on purpose: the emailed link carries the token in the URL fragment. Declared outside
      // the authenticated shell and before `quotes/:quoteId` so it is never matched as a quote id.
      path: 'quotes/view',
      pathMatch: 'full',
      title: 'titles.quote',
      loadComponent: () =>
        import('./features/quotes/pages/quote-approval/quote-approval').then(
          (m) => m.QuoteApproval,
        ),
    },
    'quotes',
  ),
  // Public on purpose: the emailed invoice link; declared before the shell so `invoices/view` is
  // never matched as an invoice id (invoice-draft-delivery BR-23).
  publicInvoiceRoute,
  withI18n(
    {
      // Customer portal: its own public pages and a guarded shell with the portal session. Declared
      // before the pathless shells so `portal/...` never reaches them (customer-portal-dashboard).
      path: 'portal',
      loadChildren: () => import('./features/portal/portal.routes'),
    },
    'portal',
  ),
  withI18n(
    {
      // Public legal pages (Privacy, Terms, public Help) with their own header and footer.
      path: '',
      loadChildren: () => import('./features/legal/legal.routes'),
    },
    'legal',
    'help',
  ),
  {
    // Old emailed links: the redirect keeps the URL fragment (customer-quote-approval BR-02).
    path: 'quote-approval',
    pathMatch: 'full',
    redirectTo: 'quotes/view',
  },
  withI18n(
    {
      // Routes declare their own full paths (public registration lives at `auth/register-company`).
      path: '',
      loadChildren: () => import('./features/organizations/organizations.routes'),
    },
    'organizations',
  ),
  withI18n(
    {
      // Pathless authenticated shell: the guard revalidates the session on every
      // navigation between its children and resolves before the shell renders.
      path: '',
      canActivate: [authGuard],
      runGuardsAndResolvers: 'always',
      loadComponent: () => import('./layout/app-shell/app-shell').then((m) => m.AppShell),
      children: [
        withI18n(
          {
            path: 'overview',
            loadChildren: () => import('./features/overview/overview.routes'),
          },
          'overview',
        ),
        withI18n(
          {
            path: 'account',
            loadChildren: () => import('./features/account/account.routes'),
          },
          'account',
        ),
        withI18n(
          {
            path: 'help',
            loadChildren: () => import('./features/help/help.routes'),
          },
          'help',
        ),
        dashboardRoute,
        // help-support-center FR-01: the retired Coming soon page of Help leads to the center.
        { path: 'coming-soon/help', pathMatch: 'full', redirectTo: '/help' },
        // BR-23: the retired Coming soon page of Reports leads to the workspace (declared first).
        { path: 'coming-soon/reports', pathMatch: 'full', redirectTo: '/reports' },
        comingSoonRoute,
        companyAdminRoute,
        businessHoursAdminRoute,
        usersAdminRoute,
        productsServicesAdminRoute,
        customersRoute,
        customerDetailRoute,
        requestsRoute,
        assessmentRoute,
        quotesRoute,
        notificationsRoute,
        staffMessagesRoute,
        auditLogRoute,
        reportsRoute,
        quoteEditRoute,
        quoteViewRoute,
        workOrderEditorRoute,
        jobsRoute,
        jobDetailRoute,
        teamRoute,
        technicianProfileRedirectRoute,
        technicianOverviewRoute,
        technicianJobHistoryRoute,
        skillsAvailabilityRoute,
        scheduleRoute,
        invoicesHubRoute,
        billingReviewRoute,
        invoiceDetailRoute,
      ],
    },
    'notifications',
    'requests',
    'invoices',
    'jobs',
    'quotes',
  ),
  withI18n(
    {
      // Dedicated technician shell (Today's jobs): same guard rules as the desktop shell. Declared
      // after it; paths that match no child fall through to the next sibling.
      path: '',
      canActivate: [authGuard],
      runGuardsAndResolvers: 'always',
      loadComponent: () =>
        import('./layout/technician-shell/technician-shell').then((m) => m.TechnicianShell),
      children: TECHNICIAN_SHELL_ROUTES,
    },
    'technician-jobs',
  ),
  { path: '**', redirectTo: 'auth/sign-in' },
];
