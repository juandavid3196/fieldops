import { Route } from '@angular/router';

import { quoteEditorUnsavedChangesGuard } from './guards/quote-editor-unsaved-changes.guard';
import { workOrderEditorUnsavedChangesGuard } from './guards/work-order-editor-unsaved-changes.guard';

import { withI18n } from '../../core/i18n/i18n-scopes';
/**
 * Quote editor (`/quotes/:quoteId/edit`); mounted inside the app shell, which owns the auth guard.
 * Not role-guarded: read roles and technicians get the forbidden state inside the page (BR-07).
 */
export const quoteEditRoute: Route = withI18n(
  {
    path: 'quotes/:quoteId/edit',
    pathMatch: 'full',
    title: 'titles.quote',
    canDeactivate: [quoteEditorUnsavedChangesGuard],
    loadComponent: () => import('./pages/quote-editor/quote-editor').then((m) => m.QuoteEditor),
  },
  'quotes',
);

/**
 * Work order editor (`/quotes/:quoteId/work-order`, Design 5); managers only, enforced in the page
 * (forbidden state) and by the backend. The unsaved-changes guard also covers Cancel.
 */
export const workOrderEditorRoute: Route = withI18n(
  {
    path: 'quotes/:quoteId/work-order',
    pathMatch: 'full',
    title: 'titles.createWorkOrder',
    canDeactivate: [workOrderEditorUnsavedChangesGuard],
    loadComponent: () =>
      import('./pages/work-order-editor/work-order-editor').then((m) => m.WorkOrderEditor),
  },
  'quotes',
);

/** Read-only quote view (`/quotes/:quoteId`, BR-30); declared after the editor route. */
export const quoteViewRoute: Route = withI18n(
  {
    path: 'quotes/:quoteId',
    pathMatch: 'full',
    title: 'titles.quote',
    loadComponent: () => import('./pages/quote-view/quote-view').then((m) => m.QuoteView),
  },
  'quotes',
);

/**
 * Quotes workspace (`/quotes`, Design 31); mounted inside the app shell, which owns the auth guard.
 * Not role-guarded: roles without Read get the forbidden state inside the page.
 */
export const quotesRoute: Route = withI18n(
  {
    path: 'quotes',
    pathMatch: 'full',
    title: 'titles.quotes',
    loadComponent: () => import('./pages/quotes/quotes').then((m) => m.Quotes),
  },
  'quotes',
);
