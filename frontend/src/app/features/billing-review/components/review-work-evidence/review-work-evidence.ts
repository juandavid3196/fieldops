import { Component, computed, inject, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { WorkEvidenceVisit } from '../../models/billing-review.model';
import { BillingReviewService } from '../../services/billing-review.service';
import {
  ACK_METHOD_LABELS,
  EVIDENCE_GROUPS,
  RELATIONSHIP_LABELS,
} from '../../utils/billing-review-format';

import { t } from '../../../../core/i18n/translate';
/** Work evidence tab (BR-12): per-visit checklist, materials, photos, summary and acknowledgment. */
@Component({
  selector: 'app-review-work-evidence',
  imports: [TranslocoPipe],
  templateUrl: './review-work-evidence.html',
  styleUrl: './review-work-evidence.scss',
})
export class ReviewWorkEvidence {
  private readonly api = inject(BillingReviewService);

  readonly workOrderId = input.required<string>();
  readonly visits = input.required<readonly WorkEvidenceVisit[]>();

  protected readonly view = computed(() =>
    this.visits().map((visit) => {
      const required = visit.checklist.filter((task) => task.required);
      const hasBefore = visit.evidence.some((item) => item.type === 'before');
      const hasAfter = visit.evidence.some((item) => item.type === 'after');
      const ack = visit.acknowledgment;
      return {
        visit,
        tasks: {
          met: required.every((task) => task.completed),
          text: t('billingReview.reviewWorkEvidence.requiredTasks', {
            value: required.filter((task) => task.completed).length,
            length: required.length,
          }),
        },
        materials: t('billingReview.reviewWorkEvidence.recorded', {
          length: visit.materials.length,
          value:
            visit.materials.length === 1
              ? t('billingReview.reviewWorkEvidence.material')
              : t('billingReview.reviewWorkEvidence.materials'),
        }),
        photos: {
          met: hasBefore && hasAfter,
          text:
            hasBefore && hasAfter
              ? t('billingReview.reviewWorkEvidence.beforeAfterEvidence')
              : t('billingReview.reviewWorkEvidence.beforeAfterEvidenceMissing'),
        },
        groups: EVIDENCE_GROUPS.map((group) => {
          const items = visit.evidence.filter((item) => item.type === group.type);
          return {
            label: group.label,
            items,
            count: `${items.length} ${items.length === 1 ? t('billingReview.reviewWorkEvidence.photo') : t('billingReview.reviewWorkEvidence.photos')}`,
          };
        }).filter((group) => group.items.length > 0),
        ack: ack && {
          ...ack,
          title: ack.signatureCaptured
            ? t('billingReview.reviewWorkEvidence.signatureCaptured')
            : (ACK_METHOD_LABELS[ack.method] ?? ack.method),
          relationship: ack.relationship
            ? (RELATIONSHIP_LABELS[ack.relationship] ?? ack.relationship)
            : null,
        },
      };
    }),
  );

  protected evidenceUrl(evidenceId: string): string {
    return this.api.evidenceUrl(this.workOrderId(), evidenceId);
  }
}
