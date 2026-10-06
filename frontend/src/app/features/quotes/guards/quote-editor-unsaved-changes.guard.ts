import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { SessionService } from '../../../core/services/session.service';
import type { QuoteEditor } from '../pages/quote-editor/quote-editor';

/** Delegates to the editor's dirty-form confirmation; a cleared session (sign-out) leaves without prompting. */
export const quoteEditorUnsavedChangesGuard: CanDeactivateFn<QuoteEditor> = (component) =>
  inject(SessionService).session() === null ? true : component.canLeave();
