import { Component, computed, input } from '@angular/core';

import { TeamCapacity } from '../../models/business-overview.model';
import { hours, overtimeChange, percentWhole } from '../../utils/business-view';
import { DeltaBadge } from '../delta-badge/delta-badge';

/** BR-17: technicians, utilization, scheduled overtime (with change) and the next 7 days. */
@Component({
  selector: 'app-team-capacity',
  imports: [DeltaBadge],
  templateUrl: './team-capacity.html',
  styleUrl: './team-capacity.scss',
})
export class TeamCapacityCard {
  readonly team = input.required<TeamCapacity>();
  readonly compare = input.required<boolean>();

  readonly rows = computed(() => {
    const team = this.team();
    return [
      {
        id: 'technicians',
        label: 'Technicians',
        icon: 'pi-users',
        value: String(team.technicians),
      },
      {
        id: 'utilization',
        label: 'Utilization',
        icon: 'pi-chart-bar',
        value: percentWhole(team.utilizationPercent),
      },
      {
        id: 'overtime',
        label: 'Scheduled overtime',
        icon: 'pi-clock',
        value: hours(team.overtimeMinutes),
      },
      {
        id: 'next7',
        label: 'Next 7 days (scheduled)',
        icon: 'pi-calendar',
        value: percentWhole(team.next7DaysPercent),
      },
    ];
  });
  readonly overtimeDelta = computed(() =>
    this.compare()
      ? overtimeChange(this.team().overtimeMinutes, this.team().previousOvertimeMinutes)
      : null,
  );
}
