import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ActivityRow } from '../../models/portal.model';
import { PortalActivityTable } from '../portal-activity-table/portal-activity-table';

/** Recent activity card (BR-26): the latest five completed services. */
@Component({
  selector: 'app-activity-card',
  imports: [TranslocoPipe, PortalActivityTable, RouterLink],
  template: `
    <section class="pcard" aria-labelledby="activity-title">
      <header class="pcard__head">
        <h2 class="pcard__title" id="activity-title">
          <i class="pi pi-clock pcard__icon" aria-hidden="true"></i>
          {{ 'portal.activityCard.recentActivity' | transloco }}
        </h2>
        <a class="pcard__link" routerLink="/portal/activity">{{
          'portal.activityCard.viewAll' | transloco
        }}</a>
      </header>
      @if (latest().length === 0) {
        <p class="pcard__empty">{{ 'portal.activityCard.noCompletedServicesYet' | transloco }}</p>
      } @else {
        <app-portal-activity-table [rows]="latest()" />
      }
    </section>
  `,
  styleUrl: '../../portal.scss',
})
export class ActivityCard {
  readonly rows = input.required<readonly ActivityRow[]>();
  protected readonly latest = computed(() => this.rows().slice(0, 5));
}
