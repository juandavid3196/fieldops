import { Component, input } from '@angular/core';

import { Step } from '../../utils/visit-detail';

/** BR-12 stepper: each step exposes done/current/upcoming as text, never by color alone. */
@Component({
  selector: 'app-visit-stepper',
  templateUrl: './visit-stepper.html',
  styleUrl: './visit-stepper.scss',
})
export class VisitStepper {
  readonly steps = input.required<readonly Step[]>();

  /** Design 8 step glyphs; a completed step always shows a check. */
  icon(label: string): string {
    switch (label) {
      case 'On the way':
        return 'pi-car';
      case 'Arrived':
        return 'pi-map-marker';
      case 'In progress':
        return 'pi-clipboard';
      default:
        return 'pi-calendar';
    }
  }
}
