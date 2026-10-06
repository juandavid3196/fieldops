import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { formatMoney } from '../../../customers/utils/customer-detail-format';
import { WorkOrderEditor } from '../../../jobs/models/work-order.model';
import { customerInitials, dateTimeAt } from '../../utils/quote-format';
import { WorkOrderPhoto, WorkOrderPhotos } from '../work-order-photos/work-order-photos';

export const NO_ASSESSMENT_RECORDED_MESSAGE = 'No assessment recorded for this quote.';
export const NO_DIAGNOSIS_MESSAGE = 'No diagnosis recorded.';

/** Locked context cards of the work order editor (BR-06): customer, approved quote, assessment. */
@Component({
  selector: 'app-work-order-context',
  imports: [RouterLink, WorkOrderPhotos],
  templateUrl: './work-order-context.html',
  styleUrl: './work-order-context.scss',
})
export class WorkOrderContext {
  readonly editor = input.required<WorkOrderEditor>();
  readonly photos = input.required<readonly WorkOrderPhoto[]>();

  readonly noAssessment = NO_ASSESSMENT_RECORDED_MESSAGE;
  readonly noDiagnosis = NO_DIAGNOSIS_MESSAGE;
  readonly customerInitials = computed(() => customerInitials(this.editor().customer.name));
  readonly technicianInitials = computed(() => {
    const name = this.editor().assessment?.technicianName ?? null;
    return name === null ? '' : customerInitials(name);
  });
  readonly approvedAt = computed(() => {
    const editor = this.editor();
    return `Approved ${dateTimeAt(editor.quote.approvedAt, editor.organizationTimezone)}`;
  });
  readonly approvedTotal = computed(() => {
    const { quote } = this.editor();
    return formatMoney(quote.approvedTotal, quote.currency);
  });
}
