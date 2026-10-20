import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { SessionService } from '../../../core/services/session.service';
import type { BusinessHours } from '../pages/business-hours/business-hours';

/** Delegates to the page's dirty check and discard confirmation; sign-out leaves without prompting. */
export const businessHoursUnsavedChangesGuard: CanDeactivateFn<BusinessHours> = (component) =>
  inject(SessionService).session() === null ? true : component.canLeave();
