import { Component, input, output } from '@angular/core';
import { ExclamationTriangleIcon } from 'primeng/icons/exclamationtriangle';
import { Message } from 'primeng/message';

/** One field error link shown in the summary (plain fragment anchor, not routerLink). */
export interface FieldErrorLink {
  readonly fieldId: string;
  readonly label: string;
  readonly message: string;
  /** When set, the link runs an action (for example opening a dialog) instead of jumping to a field. */
  readonly action?: 'sequences';
}

/**
 * Error summary (`role="alert"`, FR-02/FR-12): an optional leading
 * `ApiError.message` line plus a list of field links. The page decides
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
  readonly linkAction = output<FieldErrorLink>();

  onLinkClick(event: Event, link: FieldErrorLink): void {
    if (link.action !== undefined) {
      event.preventDefault();
      this.linkAction.emit(link);
    }
  }
}
