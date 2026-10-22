import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { TeamTab } from '../../models/team-planning.model';

/**
 * Tab row of the Team card (BR-01): Team members, Availability and Skills switch content in
 * place; the selection lives in the `tab` query parameter. Trailing controls are projected.
 */
@Component({
  selector: 'app-team-tabs',
  imports: [TranslocoPipe, RouterLink],
  templateUrl: './team-tabs.html',
  styleUrl: './team-tabs.scss',
})
export class TeamTabs {
  readonly current = input.required<TeamTab>();
  readonly membersCount = input<number | null>(null);

  readonly availabilityQuery = { tab: 'availability' };
  readonly skillsQuery = { tab: 'skills' };
}
