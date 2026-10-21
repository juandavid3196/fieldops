import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

export interface HelpCrumb {
  readonly label: string;
  /** Absent on the current page. */
  readonly link?: string;
}

/** Labelled breadcrumb landmark; the last item is the current page. */
@Component({
  selector: 'app-help-breadcrumb',
  imports: [TranslocoPipe, RouterLink],
  templateUrl: './help-breadcrumb.html',
  styleUrl: './help-breadcrumb.scss',
})
export class HelpBreadcrumb {
  readonly items = input.required<readonly HelpCrumb[]>();
}
