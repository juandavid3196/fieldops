import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { PortalAppointment } from '../../models/portal.model';
import { dateLabel, longDateLabel, timeRange } from '../../utils/portal-format';
import { appointmentNotice } from '../../utils/portal-status';
import { PortalProgressStepper } from '../portal-progress-stepper/portal-progress-stepper';

/** Upcoming appointment card (BR-22) with the progress stepper (BR-38). */
@Component({
  selector: 'app-appointment-card',
  imports: [ButtonDirective, PortalProgressStepper, RouterLink],
  templateUrl: './appointment-card.html',
  styleUrls: ['../../portal.scss', '../../portal-card.scss'],
})
export class AppointmentCard {
  readonly appointment = input<PortalAppointment | null>(null);
  readonly reschedule = output<PortalAppointment>();

  protected readonly view = computed(() => {
    const visit = this.appointment();
    if (visit === null) {
      return null;
    }
    const window = visit.arrivalWindow;
    return {
      date: longDateLabel(visit.date),
      time:
        window === null
          ? timeRange(visit.startTime, visit.endTime)
          : timeRange(window.start, window.end),
      isWindow: window !== null,
      notice: appointmentNotice(visit),
      requestedOn:
        visit.rescheduleRequestedOn === null ? null : dateLabel(visit.rescheduleRequestedOn),
    };
  });
}
