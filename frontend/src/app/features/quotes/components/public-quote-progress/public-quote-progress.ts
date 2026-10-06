import { Component, computed, input } from '@angular/core';

import { PublicQuote } from '../../models/public-quote.model';
import { dateOnlyLabel } from '../../utils/quote-format';

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
  templateUrl: './public-quote-progress.html',
  styleUrl: './public-quote-progress.scss',
})
export class PublicQuoteProgress {
  readonly quote = input.required<PublicQuote>();

  readonly steps = computed<readonly Step[]>(() => {
    const { progress, response, quote } = this.quote();
    const steps: Step[] = [
      {
        label: 'Request submitted',
        date: dateOnlyLabel(progress.requestSubmittedOn),
        state: 'completed',
        stateText: 'Completed',
      },
    ];
    if (progress.assessmentCompletedOn !== null) {
      steps.push({
        label: 'Assessment',
        date: dateOnlyLabel(progress.assessmentCompletedOn),
        state: 'completed',
        stateText: 'Completed',
      });
    }
    const answered = response === null ? '' : dateOnlyLabel(response.respondedOn);
    if (quote.status === 'approved' || quote.status === 'rejected') {
      steps.push({
        label: 'Quote',
        date: `${quote.status === 'approved' ? 'Approved' : 'Declined'} ${answered}`,
        state: 'completed',
        stateText: 'Completed',
      });
    } else {
      steps.push({ label: 'Quote', date: '', state: 'current', stateText: 'Current' });
    }
    steps.push({
      label: 'Schedule service',
      date: 'After approval',
      state: 'upcoming',
      stateText: 'Next',
    });
    return steps;
  });
}
