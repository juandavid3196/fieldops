import { Route } from '@angular/router';

import { quoteEditorUnsavedChangesGuard } from './guards/quote-editor-unsaved-changes.guard';

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

/** Read-only quote view (`/quotes/:quoteId`, BR-30); declared after the editor route. */
export const quoteViewRoute: Route = {
  path: 'quotes/:quoteId',
  pathMatch: 'full',
  title: 'Quote · FieldOps',
  loadComponent: () => import('./pages/quote-view/quote-view').then((m) => m.QuoteView),
};
