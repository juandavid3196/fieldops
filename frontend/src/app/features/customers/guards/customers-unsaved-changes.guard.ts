import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { SessionService } from '../../../core/services/session.service';
import type { Customers } from '../pages/customers/customers';

/** Delegates to the page's dirty-drawer confirmation; a cleared session (sign-out) leaves without prompting. */
export const customersUnsavedChangesGuard: CanDeactivateFn<Customers> = (component) =>
  inject(SessionService).session() === null ? true : component.canLeave();
