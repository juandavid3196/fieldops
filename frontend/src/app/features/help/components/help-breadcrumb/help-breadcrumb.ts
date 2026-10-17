import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

export interface HelpCrumb {
  readonly label: string;
  /** Absent on the current page. */
  readonly link?: string;
}

/** Labelled breadcrumb landmark; the last item is the current page. */
@Component({
  selector: 'app-help-breadcrumb',
  imports: [RouterLink],
  templateUrl: './help-breadcrumb.html',
  styleUrl: './help-breadcrumb.scss',
})
export class HelpBreadcrumb {
  readonly items = input.required<readonly HelpCrumb[]>();
}
