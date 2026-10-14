import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { TechnicianAvailability } from '../../models/dashboard.model';
import { TEAM_LINK } from '../../utils/dashboard-params';
import { technicianRow } from '../../utils/dashboard-view';

export const TECHNICIAN_ROW_LIMIT = 6;

/** BR-12: each in-scope technician's skills, status, workload and next available time. */
@Component({
  selector: 'app-dashboard-technicians',
  imports: [RouterLink],
  templateUrl: './dashboard-technicians.html',
  styleUrl: './dashboard-technicians.scss',
})
export class DashboardTechnicians {
  readonly technicians = input.required<readonly TechnicianAvailability[]>();
  readonly isToday = input.required<boolean>();
  readonly timezone = input.required<string>();

  readonly teamLink = TEAM_LINK;
  readonly rows = computed(() =>
    this.technicians()
      .slice(0, TECHNICIAN_ROW_LIMIT)
      .map((tech) => technicianRow(tech, this.isToday(), this.timezone())),
  );
}
