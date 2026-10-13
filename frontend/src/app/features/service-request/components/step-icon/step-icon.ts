import { Component, computed, input } from '@angular/core';

import { WizardStep } from '../../models/service-request.model';

/** Filled glyphs of the review and summary sections (Design 27); PrimeIcons only has outlines. */
const PATHS: Readonly<Record<WizardStep, string>> = {
  contact: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8zm0 2c-2.7 0-8 1.3-8 4v2h16v-2c0-2.7-5.3-4-8-4z',
  property: 'M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z',
  service:
    'M22.7 19l-9.1-9.1c.9-2.3.4-5-1.5-6.9-2-2-5-2.4-7.4-1.3L9 6 6 9 1.6 4.7C.4 7.1.9 10.1 2.9 12.1c1.9 1.9 4.6 2.4 6.9 1.5l9.1 9.1c.4.4 1 .4 1.4 0l2.3-2.3c.5-.4.5-1.1.1-1.4z',
  availability:
    'M19 4h-1V2h-2v2H8V2H6v2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2zm0 16H5V9h14v11zM7 11h5v5H7z',
  review: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm-2 15-5-5 1.4-1.4 3.6 3.6 7.6-7.6L19 8l-9 9z',
};

@Component({
  selector: 'app-sr-step-icon',
  template: `
    <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path [attr.d]="path()" /></svg>
  `,
  styles: `
    :host {
      display: inline-flex;
    }

    svg {
      inline-size: 1.125rem;
      block-size: 1.125rem;
      fill: currentcolor;
    }
  `,
})
export class StepIcon {
  readonly step = input.required<WizardStep>();
  protected readonly path = computed(() => PATHS[this.step()]);
}
