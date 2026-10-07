import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { SessionService } from '../../../core/services/session.service';
import type { SchedulePage } from '../pages/schedule/schedule';

/** Delegates to the page's dirty-drawer confirmation; a cleared session (sign-out) leaves without prompting. */
export const scheduleUnsavedChangesGuard: CanDeactivateFn<SchedulePage> = (component) =>
  inject(SessionService).session() === null ? true : component.canLeave();
