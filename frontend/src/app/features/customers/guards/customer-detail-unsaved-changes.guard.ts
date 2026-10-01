import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { SessionService } from '../../../core/services/session.service';
import type { CustomerDetail } from '../pages/customer-detail/customer-detail';

/** Delegates to the page's dirty-drawer confirmation; a cleared session (sign-out) leaves without prompting. */
export const customerDetailUnsavedChangesGuard: CanDeactivateFn<CustomerDetail> = (component) =>
  inject(SessionService).session() === null ? true : component.canLeave();
