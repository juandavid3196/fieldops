import { CanDeactivateFn } from '@angular/router';

import type { RegisterCompany } from '../pages/register-company/register-company';

/** FR-12: leaving the wizard with changed data asks for confirmation; success bypasses it. */
export const registerCompanyLeaveGuard: CanDeactivateFn<RegisterCompany> = (component) =>
  component.canLeave();
