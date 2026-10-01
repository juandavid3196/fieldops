import { Component } from '@angular/core';
import { Confirmation } from 'primeng/api';

import { ConfirmDialog } from '../confirm-dialog/confirm-dialog';

/** Routes a confirmation to the discard-changes dialog instead of the page's generic one. */
export const DISCARD_CHANGES_KEY = 'discard-changes';

export interface DiscardChangesOptions {
  /** What holds the edits, e.g. "this customer" or "your registration". */
  readonly subject: string;
  readonly accept: () => void;
  readonly reject?: () => void;
}

/**
 * Confirmation for leaving or closing with unsaved edits. Accept discards; reject, the close
 * button and Escape keep editing.
 */
export function discardChangesConfirmation(options: DiscardChangesOptions): Confirmation {
  return {
    key: DISCARD_CHANGES_KEY,
    header: 'Discard unsaved changes?',
    message: `You have unsaved changes in ${options.subject}. If you leave now, those changes will be lost.`,
    defaultFocus: 'reject',
    acceptButtonProps: { label: 'Discard changes', severity: 'secondary', outlined: true },
    rejectButtonProps: { label: 'Keep editing' },
    accept: options.accept,
    reject: options.reject,
  };
}

/**
 * Shared "Discard unsaved changes?" dialog. Place one per page next to the page's
 * `ConfirmationService` provider; drawers rendered inside the page reuse it.
 */
@Component({
  selector: 'app-discard-changes-dialog',
  imports: [ConfirmDialog],
  template: `<app-confirm-dialog [key]="key" note="This action can't be undone." acceptFirst />`,
})
export class DiscardChangesDialog {
  protected readonly key = DISCARD_CHANGES_KEY;
}
