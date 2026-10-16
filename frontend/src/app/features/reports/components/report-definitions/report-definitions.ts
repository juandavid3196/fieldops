import { Component, input } from '@angular/core';

import { ReportMeta } from '../../utils/report-catalog';

/** BR-20: the Report definitions panel; its text comes from the report catalog. */
@Component({
  selector: 'app-report-definitions',
  templateUrl: './report-definitions.html',
  styleUrl: './report-definitions.scss',
})
export class ReportDefinitions {
  readonly meta = input.required<ReportMeta>();
}
