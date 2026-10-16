import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { Observable } from 'rxjs';

import { SessionService } from '../../../core/services/session.service';

/** The account pages expose their dirty-check and confirmation flow as `canLeave()`. */
export interface AccountPageLeave {
  canLeave(): boolean | Observable<boolean>;
}

/**
 * FR-12: delegates to the page's own dirty-check and discard dialog.
 * Shell FR-09: a cleared session (sign-out) leaves without prompting.
 */
export const accountUnsavedChangesGuard: CanDeactivateFn<AccountPageLeave> = (component) =>
  inject(SessionService).session() === null ? true : component.canLeave();
