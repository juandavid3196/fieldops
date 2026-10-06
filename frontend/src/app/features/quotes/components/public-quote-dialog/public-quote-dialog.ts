import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Textarea } from 'primeng/textarea';

import { FormField } from '../../../organizations/components/form-field/form-field';

export type PublicQuoteDialogKind = 'ask' | 'decline';

export const PUBLIC_TEXT_LIMIT = 1000;

interface DialogCopy {
  readonly header: string;
  readonly label: string;
  readonly submit: string;
  readonly required: string;
  readonly tooLong: string;
}

const COPY: Readonly<Record<PublicQuoteDialogKind, DialogCopy>> = {
  ask: {
    header: 'Ask a question',
    label: 'Question',
    submit: 'Send question',
    required: 'Enter your question.',
    tooLong: 'Question must be 1,000 characters or fewer.',
  },
  decline: {
    header: 'Decline this quote?',
    label: 'Reason',
    submit: 'Decline quote',
    required: "Tell us why you're declining.",
    tooLong: 'Reason must be 1,000 characters or fewer.',
  },
};

/** Validation of the dialog text (BR-14, BR-15): 1 to 1,000 characters after trim. */
export function publicTextError(kind: PublicQuoteDialogKind, value: string): string | null {
  const length = value.trim().length;
  if (length === 0) {
    return COPY[kind].required;
  }
  return length > PUBLIC_TEXT_LIMIT ? COPY[kind].tooLong : null;
}

/** Ask a question and Decline dialogs. Collects and validates; the page runs the request. */
@Component({
  selector: 'app-public-quote-dialog',
  imports: [FormsModule, ButtonDirective, Dialog, SpinnerIcon, Textarea, FormField],
  templateUrl: './public-quote-dialog.html',
  styleUrl: './public-quote-dialog.scss',
})
export class PublicQuoteDialog {
  private readonly injector = inject(Injector);
  private readonly field = viewChild<ElementRef<HTMLTextAreaElement>>('field');

  readonly kind = input<PublicQuoteDialogKind | null>(null);
  readonly organizationName = input.required<string>();
  readonly submitting = input(false);
  /** Inline `400` message of the field, or the generic send failure (input is kept). */
  readonly serverError = input<string | null>(null);
  readonly failure = input<string | null>(null);

  readonly dismissed = output<void>();
  readonly submitted = output<string>();

  readonly text = signal('');
  private readonly localError = signal<string | null>(null);
  readonly open = computed(() => this.kind() !== null);
  readonly copy = computed(() => COPY[this.kind() ?? 'ask']);
  readonly error = computed(() => this.localError() ?? this.serverError());
  readonly limit = PUBLIC_TEXT_LIMIT;

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => {
          this.text.set('');
          this.localError.set(null);
        });
      }
    });
    effect(() => {
      if (this.serverError() !== null) {
        afterNextRender(() => this.field()?.nativeElement.focus(), { injector: this.injector });
      }
    });
  }

  submit(): void {
    const kind = this.kind();
    if (kind === null || this.submitting()) {
      return;
    }
    const error = publicTextError(kind, this.text());
    this.localError.set(error);
    if (error === null) {
      this.submitted.emit(this.text().trim());
    } else {
      this.field()?.nativeElement.focus();
    }
  }
}
