import { Component, computed, inject, input, linkedSignal, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Textarea } from 'primeng/textarea';
import { TranslocoPipe } from '@jsverse/transloco';

import { BillingReviewDetail, NOTE_MAX_LENGTH } from '../../models/billing-review.model';
import { BillingReviewService } from '../../services/billing-review.service';
import { LINE_STATUS, formatDateTime, money } from '../../utils/billing-review-format';
import { ReviewAuditTrail } from '../review-audit-trail/review-audit-trail';
import { ReviewWorkEvidence } from '../review-work-evidence/review-work-evidence';

import { localizedList, t } from '../../../../core/i18n/translate';
type DetailTab = 'billing' | 'work' | 'audit';

const TABS: readonly { readonly code: DetailTab; readonly label: string }[] = localizedList(() => [
  { code: 'billing', label: t('billingReview.reviewDetail.billingReview') },
  { code: 'work', label: t('billingReview.reviewDetail.workEvidence') },
  { code: 'audit', label: t('billingReview.reviewDetail.auditTrail') },
]);

let nextId = 0;

/** Detail panel (BR-24): header, tabs, comparison, totals, verification, evidence and note. */
@Component({
  selector: 'app-review-detail',
  imports: [TranslocoPipe, FormsModule, Textarea, ReviewWorkEvidence, ReviewAuditTrail],
  templateUrl: './review-detail.html',
  styleUrl: './review-detail.scss',
})
export class ReviewDetail {
  private readonly api = inject(BillingReviewService);

  readonly detail = input.required<BillingReviewDetail>();
  readonly note = input('');
  readonly noteError = input<string | null>(null);
  /** BR-26: the note is read-only for roles that cannot act. */
  readonly readOnly = input(false);
  readonly noteChange = output<string>();

  protected readonly id = `review-detail-${nextId++}`;
  protected readonly tabs = TABS;
  protected readonly maxLength = NOTE_MAX_LENGTH;
  protected readonly statuses = LINE_STATUS;
  protected get adjustmentHint(): string {
    return t('billingReview.reviewDetail.adjustmentsWillBeAvailable');
  }

  /** Back to "Billing review" whenever another job is shown. */
  readonly tab = linkedSignal<string, DetailTab>({
    source: () => this.detail().header.workOrderId,
    computation: () => 'billing',
  });

  protected readonly view = computed(() => {
    const detail = this.detail();
    const zone = detail.header.timezone;
    const currency = detail.totals.currency;
    const totals = detail.totals;
    return {
      completed: formatDateTime(detail.header.completedAt, zone),
      rows: detail.lines.map((line) => ({
        line,
        amount: line.amount === null ? '—' : money(line.amount, currency),
        status: LINE_STATUS[line.status],
      })),
      warn: detail.lines.some((line) => line.status === 'over' || line.status === 'not_in_quote'),
      totals: {
        approved: money(totals.approvedSubtotal, currency),
        billable: money(totals.invoiceSubtotal, currency),
        discount: totals.discountTotal > 0 ? money(totals.discountTotal, currency) : null,
        tax: money(totals.taxTotal, currency),
        total: money(totals.invoiceTotal, currency),
        variance: money(totals.variance, currency),
      },
    };
  });

  protected evidenceUrl(evidenceId: string): string {
    return this.api.evidenceUrl(this.detail().header.workOrderId, evidenceId);
  }

  protected select(tab: DetailTab): void {
    this.tab.set(tab);
  }

  /** Arrow keys move the selection between tabs (roving tabindex). */
  protected onTabKeydown(event: KeyboardEvent, index: number): void {
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
    if (step === 0) {
      return;
    }
    event.preventDefault();
    const next = (index + step + TABS.length) % TABS.length;
    this.tab.set(TABS[next].code);
    queueMicrotask(() => document.getElementById(`${this.id}-tab-${TABS[next].code}`)?.focus());
  }
}
