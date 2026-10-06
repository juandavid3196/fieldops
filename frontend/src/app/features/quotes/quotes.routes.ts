import { Route } from '@angular/router';

import { quoteEditorUnsavedChangesGuard } from './guards/quote-editor-unsaved-changes.guard';
import { workOrderEditorUnsavedChangesGuard } from './guards/work-order-editor-unsaved-changes.guard';

/**
 * Quote editor (`/quotes/:quoteId/edit`); mounted inside the app shell, which owns the auth guard.
 * Not role-guarded: read roles and technicians get the forbidden state inside the page (BR-07).
 */
export const quoteEditRoute: Route = {
  path: 'quotes/:quoteId/edit',
  pathMatch: 'full',
  title: 'Quote · FieldOps',
  canDeactivate: [quoteEditorUnsavedChangesGuard],
  loadComponent: () => import('./pages/quote-editor/quote-editor').then((m) => m.QuoteEditor),
};

/**
 * Work order editor (`/quotes/:quoteId/work-order`, Design 5); managers only, enforced in the page
 * (forbidden state) and by the backend. The unsaved-changes guard also covers Cancel.
 */
export const workOrderEditorRoute: Route = {
  path: 'quotes/:quoteId/work-order',
  pathMatch: 'full',
  title: 'Create work order · FieldOps',
  canDeactivate: [workOrderEditorUnsavedChangesGuard],
  loadComponent: () =>
    import('./pages/work-order-editor/work-order-editor').then((m) => m.WorkOrderEditor),
};

/** Read-only quote view (`/quotes/:quoteId`, BR-30); declared after the editor route. */
export const quoteViewRoute: Route = {
  path: 'quotes/:quoteId',
  pathMatch: 'full',
  title: 'Quote · FieldOps',
  loadComponent: () => import('./pages/quote-view/quote-view').then((m) => m.QuoteView),
};
