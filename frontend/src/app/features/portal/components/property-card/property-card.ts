import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { DashboardProperty } from '../../models/portal.model';
import { dateLabel } from '../../utils/portal-format';

/** Your property card (BR-19): the selected property, or the primary one with All. */
@Component({
  selector: 'app-property-card',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './property-card.html',
  styleUrls: ['../../portal.scss', '../../portal-card.scss'],
})
export class PropertyCard {
  readonly property = input<DashboardProperty | null>(null);

  protected readonly lastService = computed(() => {
    const date = this.property()?.lastServiceOn ?? null;
    return date === null ? null : dateLabel(date);
  });
}
