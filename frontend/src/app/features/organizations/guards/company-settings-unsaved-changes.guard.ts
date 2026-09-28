import { CanDeactivateFn } from '@angular/router';

import type { CompanySetup } from '../pages/company-setup/company-setup';

/** FR-17: delegates to the page's own dirty-check and confirmation flow. */
export const companySettingsUnsavedChangesGuard: CanDeactivateFn<CompanySetup> = (component) =>
  component.canLeave();
