import { Component, input, output } from '@angular/core';

export const NOTES_MAX_LENGTH = 4000;

/** BR-17 Notes section; the draft, pending and error state live in the parent (Save and exit shares them). */
@Component({
  selector: 'app-job-notes',
  templateUrl: './job-notes.html',
  styleUrl: './job-notes.scss',
})
export class JobNotes {
  readonly value = input.required<string>();
  readonly readOnly = input(false);
  readonly pending = input(false);
  readonly saved = input(false);
  readonly error = input<string | null>(null);
  readonly valueChange = output<string>();
  /** The field lost focus: the parent saves when the text changed. */
  readonly blurred = output<void>();

  readonly maxLength = NOTES_MAX_LENGTH;
}
