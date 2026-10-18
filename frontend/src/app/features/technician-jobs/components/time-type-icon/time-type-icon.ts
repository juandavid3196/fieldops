import { Component, input } from '@angular/core';

import { TimeEntry } from '../../models/technician-visits.model';

/** Filled work / travel / break glyph, 10% larger than the 1rem PrimeIcons size. */
@Component({
  selector: 'app-time-type-icon',
  template: `
    <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      @switch (type()) {
        @case ('work') {
          <path
            d="M10 2a2 2 0 0 0-2 2v2H4a2 2 0 0 0-2 2v4h20V8a2 2 0 0 0-2-2h-4V4a2 2 0 0 0-2-2h-4Zm0 2h4v2h-4V4Zm12 9H13v1.5h-2V13H2v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6Z"
          />
        }
        @case ('travel') {
          <path
            d="M5.7 4.6A2 2 0 0 1 7.6 3h8.8a2 2 0 0 1 1.9 1.6L19.5 10H21a1 1 0 0 1 0 2h-.5v6a1.5 1.5 0 0 1-1.5 1.5h-1A1.5 1.5 0 0 1 16.5 18v-1h-9v1A1.5 1.5 0 0 1 6 19.5H5A1.5 1.5 0 0 1 3.5 18v-6H3a1 1 0 0 1 0-2h1.5l1.2-5.4ZM7.4 10h9.2l-.9-4.2a.2.2 0 0 0-.2-.2H8.5a.2.2 0 0 0-.2.2L7.4 10ZM7 15a1.25 1.25 0 1 0 0-2.5A1.25 1.25 0 0 0 7 15Zm10 0a1.25 1.25 0 1 0 0-2.5A1.25 1.25 0 0 0 17 15Z"
          />
        }
        @default {
          <path
            d="M7 4h3.5a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1Zm6.5 0H17a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-3.5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1Z"
          />
        }
      }
    </svg>
  `,
  styles: `
    :host {
      display: inline-flex;
    }

    svg {
      inline-size: 1.1rem;
      block-size: 1.1rem;
    }
  `,
})
export class TimeTypeIcon {
  readonly type = input.required<TimeEntry['type']>();
}
