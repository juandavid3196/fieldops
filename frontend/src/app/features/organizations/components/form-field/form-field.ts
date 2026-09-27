import { Component, input } from '@angular/core';
import { ExclamationTriangleIcon } from 'primeng/icons/exclamationtriangle';

/**
 * Label-above field wrapper (design 17 pattern): label, `*` for required
 * fields, projected control, then helper OR error underneath (never both).
 * The error paragraph's `id` is `{controlId}-error`; the field's own
 * `aria-describedby` must reference that same string.
 */
@Component({
  selector: 'app-form-field',
  imports: [ExclamationTriangleIcon],
  templateUrl: './form-field.html',
  styleUrl: './form-field.scss',
})
export class FormField {
  readonly label = input.required<string>();
  readonly controlId = input.required<string>();
  readonly required = input(false);
  readonly error = input<string | null>(null);
  readonly helper = input<string | null>(null);
}
