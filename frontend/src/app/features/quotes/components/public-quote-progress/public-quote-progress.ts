import { Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { PublicQuote } from '../../models/public-quote.model';
import { dateOnlyLabel } from '../../utils/quote-format';

import { t } from '../../../../core/i18n/translate';
type StepState = 'completed' | 'current' | 'upcoming';

interface Step {
  readonly label: string;
  readonly date: string;
  readonly state: StepState;
  readonly stateText: string;
}

/** Quote progress of the quote link (BR-09): state conveyed by icon and text, not color alone. */
@Component({
  selector: 'app-public-quote-progress',
  imports: [TranslocoPipe],
  templateUrl: './public-quote-progress.html',
  styleUrl: './public-quote-progress.scss',
})
export class PublicQuoteProgress {
  readonly quote = input.required<PublicQuote>();

  readonly steps = computed<readonly Step[]>(() => {
    const { progress, response, quote } = this.quote();
    const steps: Step[] = [
      {
        label: t('quotes.publicQuoteProgress.requestSubmitted'),
        date: dateOnlyLabel(progress.requestSubmittedOn),
        state: 'completed',
        stateText: t('quotes.publicQuoteProgress.completed'),
      },
    ];
    if (progress.assessmentCompletedOn !== null) {
      steps.push({
        label: t('quotes.publicQuoteProgress.assessment'),
        date: dateOnlyLabel(progress.assessmentCompletedOn),
        state: 'completed',
        stateText: t('quotes.publicQuoteProgress.completed'),
      });
    }
    const answered = response === null ? '' : dateOnlyLabel(response.respondedOn);
    if (quote.status === 'approved' || quote.status === 'rejected') {
      steps.push({
        label: t('quotes.publicQuoteProgress.quote'),
        date: `${quote.status === 'approved' ? t('quotes.publicQuoteProgress.approved') : t('quotes.publicQuoteProgress.declined')} ${answered}`,
        state: 'completed',
        stateText: t('quotes.publicQuoteProgress.completed'),
      });
    } else {
      steps.push({
        label: t('quotes.publicQuoteProgress.quote'),
        date: '',
        state: 'current',
        stateText: t('quotes.publicQuoteProgress.current'),
      });
    }
    steps.push({
      label: t('quotes.publicQuoteProgress.scheduleService'),
      date: t('quotes.publicQuoteProgress.afterApproval'),
      state: 'upcoming',
      stateText: t('common.next'),
    });
    return steps;
  });
}
