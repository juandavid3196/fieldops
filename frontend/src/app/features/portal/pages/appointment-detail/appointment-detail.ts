import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { openBlob } from '../../../invoices/utils/blob-download';
import { PortalProgressStepper } from '../../components/portal-progress-stepper/portal-progress-stepper';
import { PortalRescheduleDialog } from '../../components/portal-reschedule-dialog/portal-reschedule-dialog';
import { PortalState } from '../../components/portal-state/portal-state';
import { PortalStatusChip } from '../../components/portal-status-chip/portal-status-chip';
import { PortalAppointment } from '../../models/portal.model';
import { PortalAppointmentsService } from '../../services/portal-appointments.service';
import { dateLabel, longDateLabel, timeRange } from '../../utils/portal-format';
import { PortalResource } from '../../utils/portal-resource';
import { appointmentNotice, visitChip } from '../../utils/portal-status';

const OPEN_STATUSES: readonly string[] = [
  'scheduled',
  'assigned',
  'on_the_way',
  'in_progress',
  'paused',
];

/** Appointment detail (BR-29): schedule, stepper, reschedule state and, once done, the report. */
@Component({
  selector: 'app-portal-appointment-detail',
  imports: [
    ButtonDirective,
    PortalProgressStepper,
    PortalRescheduleDialog,
    PortalState,
    PortalStatusChip,
    RouterLink,
  ],
  templateUrl: './appointment-detail.html',
  styleUrls: ['../../portal.scss', '../../portal-card.scss', '../../portal-facts.scss'],
})
export class PortalAppointmentDetail {
  private readonly appointments = inject(PortalAppointmentsService);
  private readonly visitId = inject(ActivatedRoute).snapshot.paramMap.get('visitId') ?? '';
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);

  readonly resource = new PortalResource<PortalAppointment>();
  readonly rescheduleOpen = signal(false);
  readonly reportBusy = signal(false);
  readonly reportError = signal<string | null>(null);

  readonly view = computed(() => {
    const visit = this.resource.data();
    if (visit === null) {
      return null;
    }
    const window = visit.arrivalWindow;
    return {
      visit,
      chip: visitChip(visit),
      date: longDateLabel(visit.date),
      time:
        window === null
          ? timeRange(visit.startTime, visit.endTime)
          : timeRange(window.start, window.end),
      isWindow: window !== null,
      open: OPEN_STATUSES.includes(visit.status),
      notice: appointmentNotice(visit),
      requestedOn:
        visit.rescheduleRequestedOn === null ? null : dateLabel(visit.rescheduleRequestedOn),
    };
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.resource.load(this.appointments.get(this.visitId));
  }

  rescheduled(event: { readonly requestedOn: string }): void {
    this.rescheduleOpen.set(false);
    this.resource.patch((visit) => ({
      ...visit,
      rescheduleRequestedOn: event.requestedOn,
      canRequestReschedule: false,
    }));
  }

  report(): void {
    const visit = this.resource.data();
    if (visit === null || this.reportBusy()) {
      return;
    }
    this.reportBusy.set(true);
    this.reportError.set(null);
    this.appointments
      .workOrderReport(visit.workOrderId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          openBlob(this.document, blob, `${visit.workOrderNumber}-completion-report.pdf`);
          this.reportBusy.set(false);
        },
        error: () => {
          this.reportBusy.set(false);
          this.reportError.set("We couldn't open the report. Try again.");
        },
      });
  }
}
