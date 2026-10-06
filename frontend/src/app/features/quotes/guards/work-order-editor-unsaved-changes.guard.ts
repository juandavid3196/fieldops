import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

import { SessionService } from '../../../core/services/session.service';
import type { WorkOrderEditor } from '../pages/work-order-editor/work-order-editor';

/** Delegates to the editor's dirty-form confirmation; a cleared session (sign-out) leaves without prompting. */
export const workOrderEditorUnsavedChangesGuard: CanDeactivateFn<WorkOrderEditor> = (component) =>
  inject(SessionService).session() === null ? true : component.canLeave();
