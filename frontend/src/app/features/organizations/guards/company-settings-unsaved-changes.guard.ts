import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { SessionService } from '../../../core/services/session.service';
import type { CompanySetup } from '../pages/company-setup/company-setup';

/**
 * FR-17: delegates to the page's own dirty-check and confirmation flow.
 * Shell FR-09: a cleared session (sign-out) leaves without prompting.
 */
export const companySettingsUnsavedChangesGuard: CanDeactivateFn<CompanySetup> = (component) =>
  inject(SessionService).session() === null ? true : component.canLeave();
