import { Component, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import type { ButtonDirectiveOptions } from 'primeng/types/button';

/** "Report delay or issue" dialog: Call office (when the office has a phone) and coming-soon text. */
@Component({
  selector: 'app-report-issue',
  imports: [ButtonDirective, Dialog],
  templateUrl: './report-issue.html',
  styleUrl: './report-issue.scss',
})
export class ReportIssue {
  readonly visible = input.required<boolean>();
  /** `tel:` link for the office; `null` hides Call office. */
  readonly officeHref = input<string | null>(null);
  readonly closed = output<void>();

  /** Round, filled close button from the design. */
  readonly closeProps: ButtonDirectiveOptions = {
    severity: 'secondary',
    variant: 'text',
    rounded: true,
    style: {
      width: '2.75rem',
      height: '2.75rem',
      color: 'var(--fo-color-heading)',
      background: 'var(--fo-color-surface-hover)',
    },
  };
}
