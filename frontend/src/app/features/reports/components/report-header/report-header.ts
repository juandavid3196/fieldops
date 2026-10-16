import { Component, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';

import { ReportFormat } from '../../models/reports.model';
import { ReportMeta } from '../../utils/report-catalog';

/** BR-21: star, name, description, Export CSV and Download PDF; one export runs at a time (BR-22). */
@Component({
  selector: 'app-report-header',
  imports: [ButtonDirective],
  templateUrl: './report-header.html',
  styleUrl: './report-header.scss',
})
export class ReportHeader {
  readonly meta = input.required<ReportMeta>();
  readonly favorite = input(false);
  readonly disabled = input(false);
  /** The format being exported, if any. */
  readonly exporting = input<ReportFormat | null>(null);

  readonly favoriteToggled = output<void>();
  readonly exportRequested = output<ReportFormat>();
}
