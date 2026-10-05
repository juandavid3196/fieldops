import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { SessionService } from '../../../core/services/session.service';
import type { Assessment } from '../pages/assessment/assessment';

/** Delegates to the page's dirty-form confirmation; a cleared session (sign-out) leaves without prompting. */
export const assessmentUnsavedChangesGuard: CanDeactivateFn<Assessment> = (component) =>
  inject(SessionService).session() === null ? true : component.canLeave();
