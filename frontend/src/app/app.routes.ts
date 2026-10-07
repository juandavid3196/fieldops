import { Routes } from '@angular/router';

import { authGuard } from './core/guards/auth.guard';
import { comingSoonRoute } from './features/coming-soon/coming-soon.routes';
import { customerDetailRoute, customersRoute } from './features/customers/customers.routes';
import { jobDetailRoute, jobsRoute } from './features/jobs/jobs.routes';
import {
  quoteEditRoute,
  quoteViewRoute,
  workOrderEditorRoute,
} from './features/quotes/quotes.routes';
import { companyAdminRoute } from './features/organizations/organizations.routes';
import { productsServicesAdminRoute } from './features/products-services/products-services.routes';
import { assessmentRoute, requestsRoute } from './features/requests/requests.routes';
import { skillsAvailabilityRoute, teamRoute } from './features/team/team.routes';
import { scheduleRoute } from './features/schedule/schedule.routes';
import { usersAdminRoute } from './features/users/users.routes';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'auth/sign-in' },
  {
    path: 'auth',
    loadChildren: () => import('./features/authentication/authentication.routes'),
  },
  {
    // Public on purpose: anonymous request form, outside the authenticated shell.
    path: 'request/:slug',
    loadChildren: () => import('./features/service-request/service-request.routes'),
  },
  {
    // Public on purpose: the emailed link carries the token in the URL fragment. Declared outside
    // the authenticated shell and before `quotes/:quoteId` so it is never matched as a quote id.
    path: 'quotes/view',
    pathMatch: 'full',
    title: 'Quote · FieldOps',
    loadComponent: () =>
      import('./features/quotes/pages/quote-approval/quote-approval').then((m) => m.QuoteApproval),
  },
  {
    // Old emailed links: the redirect keeps the URL fragment (customer-quote-approval BR-02).
    path: 'quote-approval',
    pathMatch: 'full',
    redirectTo: 'quotes/view',
  },
  {
    // Routes declare their own full paths (public registration lives at `auth/register-company`).
    path: '',
    loadChildren: () => import('./features/organizations/organizations.routes'),
  },
  {
    // Pathless authenticated shell: the guard revalidates the session on every
    // navigation between its children and resolves before the shell renders.
    path: '',
    canActivate: [authGuard],
    runGuardsAndResolvers: 'always',
    loadComponent: () => import('./layout/app-shell/app-shell').then((m) => m.AppShell),
    children: [
      {
        path: 'overview',
        loadChildren: () => import('./features/overview/overview.routes'),
      },
      comingSoonRoute,
      companyAdminRoute,
      usersAdminRoute,
      productsServicesAdminRoute,
      customersRoute,
      customerDetailRoute,
      requestsRoute,
      assessmentRoute,
      quoteEditRoute,
      quoteViewRoute,
      workOrderEditorRoute,
      jobsRoute,
      jobDetailRoute,
      teamRoute,
      skillsAvailabilityRoute,
      scheduleRoute,
    ],
  },
  { path: '**', redirectTo: 'auth/sign-in' },
];
