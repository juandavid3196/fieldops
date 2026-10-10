import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Observable } from 'rxjs';

import { openBlob } from '../../../invoices/utils/blob-download';
import { ActivityRow } from '../../models/portal.model';
import { PortalAppointmentsService } from '../../services/portal-appointments.service';
import { PortalInvoicesService } from '../../services/portal-invoice.service';
import { dateLabel, money } from '../../utils/portal-format';
import { activityChip } from '../../utils/portal-status';
import { PortalStatusChip } from '../portal-status-chip/portal-status-chip';

export const ACTIVITY_DOWNLOAD_ERROR = "We couldn't open the file. Try again.";

/**
 * Activity rows (BR-26) shared by Home and the Activity page: Service, Date, Reference, Amount,
 * Status and the View report / Receipt / View invoice actions. Stacked rows below 768 px.
 */
@Component({
  selector: 'app-portal-activity-table',
  imports: [ButtonDirective, PortalStatusChip, RouterLink],
  templateUrl: './portal-activity-table.html',
  styleUrls: ['../../portal.scss', '../../portal-table.scss'],
})
export class PortalActivityTable {
  private readonly appointments = inject(PortalAppointmentsService);
  private readonly invoices = inject(PortalInvoicesService);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);

  readonly rows = input.required<readonly ActivityRow[]>();

  readonly busy = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  protected readonly view = computed(() =>
    this.rows().map((row) => ({
      row,
      date: dateLabel(row.completedOn),
      amount: row.amount === null ? '—' : money(row.amount, row.currency),
      chip: activityChip(row.status),
      key: row.workOrderId,
    })),
  );

  report(row: ActivityRow): void {
    this.run(
      `report:${row.workOrderId}`,
      this.appointments.workOrderReport(row.workOrderId),
      `${row.reference}-completion-report.pdf`,
    );
  }

  receipt(row: ActivityRow): void {
    if (row.invoiceId === null || row.receiptPaymentId === null) {
      return;
    }
    this.run(
      `receipt:${row.workOrderId}`,
      this.invoices.receipt(row.invoiceId, row.receiptPaymentId),
      `${row.reference}-receipt.pdf`,
    );
  }

  private run(key: string, request: Observable<Blob>, fileName: string): void {
    if (this.busy() !== null) {
      return;
    }
    this.busy.set(key);
    this.error.set(null);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (blob) => {
        openBlob(this.document, blob, fileName);
        this.busy.set(null);
      },
      error: () => {
        this.busy.set(null);
        this.error.set(ACTIVITY_DOWNLOAD_ERROR);
      },
    });
  }
}
