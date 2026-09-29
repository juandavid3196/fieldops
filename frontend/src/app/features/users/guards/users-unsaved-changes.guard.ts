import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { SessionService } from '../../../core/services/session.service';
import type { Users } from '../pages/users/users';

/** Delegates to the page's dirty-drawer confirmation; a cleared session (sign-out) leaves without prompting. */
export const usersUnsavedChangesGuard: CanDeactivateFn<Users> = (component) =>
  inject(SessionService).session() === null ? true : component.canLeave();
