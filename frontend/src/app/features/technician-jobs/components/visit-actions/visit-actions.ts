import { Component, computed, input } from '@angular/core';
import { ButtonDirective } from 'primeng/button';

import { TodayVisit } from '../../models/technician-visits.model';
import { directionsUrl, telUrl } from '../../utils/technician-format';

/** BR-12: Call (`tel:`, hidden without a phone) and Directions (external Google Maps, new tab). */
@Component({
  selector: 'app-visit-actions',
  imports: [ButtonDirective],
  templateUrl: './visit-actions.html',
  styleUrl: './visit-actions.scss',
})
export class VisitActions {
  readonly visit = input.required<TodayVisit>();

  readonly callHref = computed(() => {
    const phone = this.visit().phone?.trim() ?? '';
    return phone === '' ? null : telUrl(phone);
  });
  readonly directionsHref = computed(() => directionsUrl(this.visit()));
}
