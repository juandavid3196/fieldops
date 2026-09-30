import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { SessionService } from '../../../core/services/session.service';
import type { ProductsServices } from '../pages/products-services/products-services';

/** Delegates to the page's dirty-drawer confirmation; a cleared session (sign-out) leaves without prompting. */
export const productsServicesUnsavedChangesGuard: CanDeactivateFn<ProductsServices> = (
  component,
) => (inject(SessionService).session() === null ? true : component.canLeave());
