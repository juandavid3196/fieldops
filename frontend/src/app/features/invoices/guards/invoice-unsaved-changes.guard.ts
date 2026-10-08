import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { SessionService } from '../../../core/services/session.service';
import type { InvoiceDetailPage } from '../pages/invoice-detail/invoice-detail';

/** Delegates to the page's dirty-form confirmation; a cleared session (sign-out) leaves without prompting. */
export const invoiceUnsavedChangesGuard: CanDeactivateFn<InvoiceDetailPage> = (component) =>
  inject(SessionService).session() === null ? true : component.canLeave();
