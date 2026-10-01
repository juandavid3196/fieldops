import { Component, computed, input, output } from '@angular/core';
import { Tag } from 'primeng/tag';

import { TeamPeriod, TeamSort, TechnicianRow } from '../../models/team.model';
import {
  STATUS_LABELS,
  WorkloadView,
  nextAvailableText,
  statusSeverity,
  workloadView,
} from '../../utils/team-format';

export interface TeamRowView {
  readonly row: TechnicianRow;
  readonly skills: string;
  readonly workload: WorkloadView;
  readonly next: string;
}

/** BR-12 table: presentational; the page owns data, sorting and the actions menu. */
@Component({
  selector: 'app-team-table',
  imports: [Tag],
  templateUrl: './team-table.html',
  styleUrl: './team-table.scss',
})
export class TeamTable {
  readonly rows = input.required<readonly TechnicianRow[]>();
  readonly sort = input<TeamSort>('name_asc');
  readonly period = input<TeamPeriod>('today');
  readonly busyId = input<string | null>(null);

  readonly sortRequested = output<void>();
  readonly nameClicked = output<TechnicianRow>();
  readonly menuRequested = output<{ readonly event: Event; readonly row: TechnicianRow }>();

  readonly statusLabels = STATUS_LABELS;
  readonly statusSeverity = statusSeverity;

  readonly ariaSort = computed(() => (this.sort() === 'name_asc' ? 'ascending' : 'descending'));
  readonly jobsHeader = computed(() =>
    this.period() === 'today' ? 'Jobs today' : 'Jobs this week',
  );
  readonly views = computed<readonly TeamRowView[]>(() =>
    this.rows().map((row) => ({
      row,
      skills: row.skills.length > 0 ? row.skills.join(', ') : '—',
      workload: workloadView(row.workloadState, row.workloadPercent),
      next: nextAvailableText(row.nextAvailable, row.timezone),
    })),
  );

  inactive(row: TechnicianRow): boolean {
    return row.profileStatus !== 'active';
  }
}
