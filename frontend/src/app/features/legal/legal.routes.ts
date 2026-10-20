import { Routes } from '@angular/router';

/**
 * Public legal and help pages linked from sign-in. No guard: they are readable with or without a
 * session. Paths that match no child fall through to the next sibling in `app.routes.ts`.
 */
export default [
  {
    path: '',
    loadComponent: () =>
      import('./components/public-site-shell/public-site-shell').then((m) => m.PublicSiteShell),
    children: [
      {
        path: 'privacy',
        title: 'Privacy Policy · FieldOps',
        loadComponent: () =>
          import('./pages/privacy-policy/privacy-policy').then((m) => m.PrivacyPolicy),
      },
      {
        path: 'terms',
        title: 'Terms of Service · FieldOps',
        loadComponent: () =>
          import('./pages/terms-of-service/terms-of-service').then((m) => m.TermsOfService),
      },
      {
        // `/help` is the authenticated center; the public copy lives here (PUBLIC_HELP_BASE).
        path: 'support',
        loadChildren: () => import('../help/help.routes').then((m) => m.createPublicHelpRoutes()),
      },
    ],
  },
] satisfies Routes;
