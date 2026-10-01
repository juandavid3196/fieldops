import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { SessionService } from '../../../core/services/session.service';
import type { Team } from '../pages/team/team';

/** Delegates to the page's dirty-form confirmation; a cleared session (sign-out) leaves without prompting. */
export const teamUnsavedChangesGuard: CanDeactivateFn<Team> = (component) =>
  inject(SessionService).session() === null ? true : component.canLeave();
