import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { HubPager } from '../../../invoices/components/hub-pager/hub-pager';
import { PortalPropertyFilter } from '../../components/portal-property-filter/portal-property-filter';
import { PortalState } from '../../components/portal-state/portal-state';
import { PortalStatusChip } from '../../components/portal-status-chip/portal-status-chip';
import { AppointmentScope } from '../../models/portal.model';
import { PortalAppointmentsService } from '../../services/portal-appointments.service';
import { dateLabel, timeRange } from '../../utils/portal-format';
import { PortalList } from '../../utils/portal-list';
import { visitChip } from '../../utils/portal-status';

/** Appointments list (BR-29): Upcoming and Past tabs, property-filterable, 20 per page. */
@Component({
  selector: 'app-portal-appointments',
  imports: [
    ButtonDirective,
    HubPager,
    PortalPropertyFilter,
    PortalState,
    PortalStatusChip,
    RouterLink,
  ],
  templateUrl: './appointments.html',
  styleUrls: ['../../portal.scss', '../../portal-table.scss'],
})
export class PortalAppointments {
  private readonly appointments = inject(PortalAppointmentsService);

  readonly scope = signal<AppointmentScope>('upcoming');
  readonly tabs: readonly { readonly scope: AppointmentScope; readonly label: string }[] = [
    { scope: 'upcoming', label: 'Upcoming' },
    { scope: 'past', label: 'Past' },
  ];
  readonly list = new PortalList((page, propertyId) =>
    this.appointments.list(this.scope(), page, propertyId),
  );
  readonly query = computed(() =>
    this.list.propertyId() === null ? {} : { propertyId: this.list.propertyId() },
  );
  readonly rows = computed(() =>
    (this.list.resource.data()?.items ?? []).map((appointment) => ({
      appointment,
      date: dateLabel(appointment.date),
      time: timeRange(appointment.startTime, appointment.endTime),
      chip: visitChip(appointment),
    })),
  );

  select(scope: AppointmentScope): void {
    if (scope === this.scope()) {
      return;
    }
    this.scope.set(scope);
    this.list.page.set(1);
    this.list.reload();
  }
}
