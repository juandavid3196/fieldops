import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { AssessmentPhotos } from '../../../requests/components/assessment-photos/assessment-photos';
import { CompletedAssessment } from '../../../requests/models/requests.model';
import { customerInitials, dateTimeAt, localZone } from '../../utils/quote-format';

import { t } from '../../../../core/i18n/translate';
export const NO_ASSESSMENT_MESSAGE = (): string =>
  t('quotes.quoteAssessmentCard.noAssessmentMessage');
export const NO_DIAGNOSIS_MESSAGE = (): string =>
  t('quotes.quoteAssessmentCard.noDiagnosisMessage');

/** Assessment summary card (BR-08). Presentational; photos open through the authenticated blob. */
@Component({
  selector: 'app-quote-assessment-card',
  imports: [TranslocoPipe, RouterLink, AssessmentPhotos],
  templateUrl: './quote-assessment-card.html',
  styleUrl: './quote-assessment-card.scss',
})
export class QuoteAssessmentCard {
  readonly requestId = input.required<string>();
  readonly assessment = input.required<CompletedAssessment | null>();

  get noAssessment(): string {
    return NO_ASSESSMENT_MESSAGE();
  }
  get noDiagnosis(): string {
    return NO_DIAGNOSIS_MESSAGE();
  }
  readonly requestQueryParams = computed(() => ({ request: this.requestId() }));
  readonly summary = computed(() => {
    const assessment = this.assessment();
    if (assessment === null) {
      return null;
    }
    const name = assessment.technician?.name ?? null;
    return {
      technician: name,
      initials: name === null ? '' : customerInitials(name),
      start: dateTimeAt(assessment.start, localZone()),
    };
  });
}
