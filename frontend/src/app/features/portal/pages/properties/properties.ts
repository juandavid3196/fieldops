import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { PortalState } from '../../components/portal-state/portal-state';
import { PortalProperty } from '../../models/portal.model';
import { PortalPropertiesService } from '../../services/portal-properties.service';
import { dateLabel } from '../../utils/portal-format';
import { PortalResource } from '../../utils/portal-resource';

/** Properties list (BR-33): active properties, primary first, with Edit and Add property. */
@Component({
  selector: 'app-portal-properties',
  imports: [ButtonDirective, PortalState, RouterLink],
  templateUrl: './properties.html',
  styleUrls: ['../../portal.scss', '../../portal-card.scss'],
})
export class PortalProperties {
  private readonly properties = inject(PortalPropertiesService);

  readonly resource = new PortalResource<readonly PortalProperty[]>(
    (properties) => properties.length === 0,
  );
  readonly rows = computed(() =>
    (this.resource.data() ?? []).map((property) => ({
      property,
      lastService: property.lastServiceOn === null ? null : dateLabel(property.lastServiceOn),
    })),
  );

  constructor() {
    this.load();
  }

  load(): void {
    this.resource.load(this.properties.list());
  }
}
