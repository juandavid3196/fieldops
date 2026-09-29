import { Component, input } from '@angular/core';

/**
 * Wizard card: icon badge, title and description over projected content. The section is labelled
 * by its heading; an optional `[cardAside]` slot sits beside the heading (for example a note).
 */
@Component({
  selector: 'app-wizard-card',
  templateUrl: './wizard-card.html',
  styleUrl: './wizard-card.scss',
})
export class WizardCard {
  readonly headingId = input.required<string>();
  readonly title = input.required<string>();
  readonly description = input.required<string>();
  readonly icon = input.required<string>();
}
