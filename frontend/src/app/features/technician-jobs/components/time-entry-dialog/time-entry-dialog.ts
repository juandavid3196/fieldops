import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';

import { TimeEntry } from '../../models/technician-visits.model';
import { TimeTypeIcon } from '../time-type-icon/time-type-icon';

export interface TimeEntryDetail {
  readonly entry: TimeEntry;
  readonly typeLabel: string;
  readonly number: string;
  readonly visitText: string;
  readonly date: string;
  readonly startText: string;
  readonly endText: string;
  readonly duration: string;
}

/** Read-only detail of one time entry; "Open job" only when the visit is still accessible. */
@Component({
  selector: 'app-time-entry-dialog',
  imports: [RouterLink, ButtonDirective, Dialog, TimeTypeIcon],
  templateUrl: './time-entry-dialog.html',
  styleUrl: './time-entry-dialog.scss',
})
export class TimeEntryDialog {
  readonly item = input.required<TimeEntryDetail | null>();
  readonly notice = input.required<string>();
  readonly closed = output<void>();
}
