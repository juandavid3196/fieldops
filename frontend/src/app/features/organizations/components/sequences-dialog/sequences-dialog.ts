import { Component, effect, input, model, output, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputNumber } from 'primeng/inputnumber';

import { validateNextInvoiceNumber } from '../../pages/register-company/register-company.validators';
import { FormField } from '../form-field/form-field';

/** The three sequence values edited in the dialog (a subset of the organization form). */
export interface SequenceValues {
  nextQuoteNumber: number | null;
  nextWorkOrderNumber: number | null;
  nextInvoiceNumber: number | null;
}

type SequenceKey = keyof SequenceValues;

/**
 * "Edit sequences" dialog (FR-11): local copy of the quote, work order and invoice next
 * numbers. **Apply** hands valid values to the page form; **Cancel** discards them.
 * Read-only users get "Sequences" with only **Close**.
 */
@Component({
  selector: 'app-sequences-dialog',
  imports: [ReactiveFormsModule, ButtonDirective, Dialog, InputNumber, FormField],
  templateUrl: './sequences-dialog.html',
  styleUrl: './sequences-dialog.scss',
})
export class SequencesDialog {
  readonly visible = model(false);
  readonly canManage = input.required<boolean>();
  /** Current values of the page form; copied into the dialog each time it opens. */
  readonly values = input.required<SequenceValues>();
  /** Server messages (`400`) for the sequence keys, shown on the fields when the dialog opens. */
  readonly serverErrors = input<Partial<Record<SequenceKey, string>>>({});
  readonly applied = output<SequenceValues>();
  /** Fired after the dialog closed (focus goes back to the opener). */
  readonly closed = output<void>();

  readonly form = new FormGroup({
    nextQuoteNumber: new FormControl<number | null>(1),
    nextWorkOrderNumber: new FormControl<number | null>(1),
    nextInvoiceNumber: new FormControl<number | null>(1),
  });
  errors: Partial<Record<SequenceKey, string>> = {};

  readonly fields: readonly { key: SequenceKey; label: string }[] = [
    { key: 'nextQuoteNumber', label: 'Next quote number' },
    { key: 'nextWorkOrderNumber', label: 'Next work order number' },
    { key: 'nextInvoiceNumber', label: 'Next invoice number' },
  ];

  constructor() {
    let wasVisible = false;
    effect(() => {
      const isVisible = this.visible();
      if (isVisible && !wasVisible) {
        untracked(() => this.open());
      }
      wasVisible = isVisible;
    });
  }

  fieldId(key: SequenceKey): string {
    return `sequence-${key}`;
  }

  apply(): void {
    if (!this.canManage()) {
      return;
    }
    const value = this.form.getRawValue();
    const errors: Partial<Record<SequenceKey, string>> = {};
    for (const { key } of this.fields) {
      const message = validateNextInvoiceNumber(value[key]);
      if (message !== null) {
        errors[key] = message;
      }
    }
    this.errors = errors;
    if (Object.keys(errors).length > 0) {
      return;
    }
    this.applied.emit(value);
    this.close();
  }

  close(): void {
    this.visible.set(false);
  }

  onHide(): void {
    this.closed.emit();
  }

  private open(): void {
    this.form.reset(this.values());
    this.errors = { ...this.serverErrors() };
    if (this.canManage()) {
      this.form.enable({ emitEvent: false });
    } else {
      this.form.disable({ emitEvent: false });
    }
  }
}
