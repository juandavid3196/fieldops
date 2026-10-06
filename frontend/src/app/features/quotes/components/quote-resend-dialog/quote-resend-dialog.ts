import { Component, computed, effect, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Textarea } from 'primeng/textarea';

import { FormField } from '../../../organizations/components/form-field/form-field';
import { EMAIL_LIMIT, emailMessageError } from '../../utils/quote-lines';

/**
 * Resend email dialog (BR-26): the message is only sent, never stored, so it is asked again.
 * Collects and validates; the page runs the request and owns `409` and failure handling.
 */
@Component({
  selector: 'app-quote-resend-dialog',
  imports: [FormsModule, ButtonDirective, Dialog, SpinnerIcon, Textarea, FormField],
  templateUrl: './quote-resend-dialog.html',
  styleUrl: './quote-resend-dialog.scss',
})
export class QuoteResendDialog {
  readonly open = input.required<boolean>();
  readonly recipient = input.required<string | null>();
  readonly initialMessage = input.required<string>();
  readonly submitting = input(false);
  /** `400` message of `emailMessage` from the server. */
  readonly serverError = input<string | null>(null);

  readonly dismissed = output<void>();
  readonly submitted = output<string>();

  readonly message = signal('');
  private readonly localError = signal<string | null>(null);
  readonly limit = EMAIL_LIMIT;
  readonly error = computed(() => this.localError() ?? this.serverError());

  constructor() {
    effect(() => {
      if (this.open()) {
        const initial = this.initialMessage();
        untracked(() => {
          this.message.set(initial);
          this.localError.set(null);
        });
      }
    });
  }

  submit(): void {
    if (this.submitting()) {
      return;
    }
    const error = emailMessageError(this.message());
    this.localError.set(error);
    if (error === null) {
      this.submitted.emit(this.message().trim());
    }
  }
}
