import { Component, booleanAttribute, input } from '@angular/core';
import { Confirmation } from 'primeng/api';
import { AutoFocus } from 'primeng/autofocus';
import { ButtonDirective, ButtonSeverity } from 'primeng/button';
import { ConfirmDialog as PrimeConfirmDialog } from 'primeng/confirmdialog';

interface ActionButton {
  readonly label: string;
  readonly severity: ButtonSeverity | undefined;
  readonly outlined: boolean;
  readonly focused: boolean;
  readonly run: () => void;
}

interface ButtonProps {
  readonly label?: string;
  readonly severity?: ButtonSeverity;
  readonly outlined?: boolean;
}

let nextId = 0;

/**
 * FieldOps confirmation dialog: warning icon, title, message, optional note and the
 * confirmation's own accept/reject buttons. Place one per `ConfirmationService` scope (and
 * one per `key`); the close button and Escape reject.
 */
@Component({
  selector: 'app-confirm-dialog',
  imports: [AutoFocus, ButtonDirective, PrimeConfirmDialog],
  templateUrl: './confirm-dialog.html',
  styleUrl: './confirm-dialog.scss',
})
export class ConfirmDialog {
  /** Routes only confirmations with this key here; none handles the scope's unkeyed ones. */
  readonly key = input<string | undefined>(undefined);
  /** Highlighted warning shown under the message. */
  readonly note = input<string | undefined>(undefined);
  /** Shows the accept button before the reject button (reject stays the primary action). */
  readonly acceptFirst = input(false, { transform: booleanAttribute });

  private readonly id = `app-confirm-dialog-${nextId++}`;
  protected readonly titleId = `${this.id}-title`;
  protected readonly messageId = `${this.id}-message`;
  protected readonly pt = {
    root: { 'aria-labelledby': this.titleId, 'aria-describedby': this.messageId },
  };

  protected danger(confirmation: Confirmation | null | undefined): boolean {
    return (confirmation?.acceptButtonProps as ButtonProps | undefined)?.severity === 'danger';
  }

  protected actions(
    confirmation: Confirmation | null | undefined,
    onAccept: () => void,
    onReject: () => void,
  ): ActionButton[] {
    const acceptProps = (confirmation?.acceptButtonProps ?? {}) as ButtonProps;
    const rejectProps = (confirmation?.rejectButtonProps ?? {}) as ButtonProps;
    const focusReject = confirmation?.defaultFocus === 'reject';
    const reject: ActionButton = {
      label: rejectProps.label ?? 'Cancel',
      severity: rejectProps.severity,
      outlined: rejectProps.outlined ?? false,
      focused: focusReject,
      run: onReject,
    };
    const accept: ActionButton = {
      label: acceptProps.label ?? 'Confirm',
      severity: acceptProps.severity,
      outlined: acceptProps.outlined ?? false,
      focused: !focusReject,
      run: onAccept,
    };
    return this.acceptFirst() ? [accept, reject] : [reject, accept];
  }
}
