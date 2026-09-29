import { Component, input } from '@angular/core';
import { ExclamationTriangleIcon } from 'primeng/icons/exclamationtriangle';
import { Message } from 'primeng/message';

/** One field error entry shown as plain text in the summary. */
export interface FieldErrorLink {
  readonly fieldId: string;
  readonly label: string;
  readonly message: string;
}

/**
 * Error summary (`role="alert"`, FR-02/FR-12): an optional leading
 * `ApiError.message` line plus a plain-text list of field errors. The page decides
 * whether to focus the summary itself (no field errors) or the first
 * invalid field (`fieldErrors` non-empty).
 */
@Component({
  selector: 'app-error-summary',
  imports: [Message, ExclamationTriangleIcon],
  templateUrl: './error-summary.html',
  styleUrl: './error-summary.scss',
})
export class ErrorSummary {
  readonly id = input.required<string>();
  readonly leadingMessage = input<string | null>(null);
  readonly links = input<readonly FieldErrorLink[]>([]);
}
