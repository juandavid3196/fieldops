import { Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Select } from 'primeng/select';
import { TranslocoPipe } from '@jsverse/transloco';

import { FilterKey, ReportFilterValues } from '../../models/reports.model';
import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';

export interface FilterField {
  readonly key: FilterKey;
  readonly label: string;
  readonly options: { readonly code: string; readonly label: string }[];
}

/** BR-19: report filters edited as a draft; Apply filters and Reset act on the whole draft. */
@Component({
  selector: 'app-report-filters',
  imports: [LocalizedRefPipe, TranslocoPipe, ButtonDirective, FormsModule, Select],
  templateUrl: './report-filters.html',
  styleUrl: './report-filters.scss',
})
export class ReportFilters {
  readonly fields = input.required<readonly FilterField[]>();
  readonly values = input<ReportFilterValues>({});
  readonly disabled = input(false);

  readonly changed = output<{ readonly key: FilterKey; readonly code: string }>();
  readonly applied = output<void>();
  readonly resetRequested = output<void>();
}
