import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Tag } from 'primeng/tag';
import { TranslocoPipe } from '@jsverse/transloco';

import { TechnicianDetail } from '../../models/team.model';
import { PROFILE_STATUS_LABELS, profileStatusSeverity } from '../../utils/team-format';
import { initialsOf } from '../../utils/technician-profile-format';

export type ProfileTab = 'overview' | 'job-history';

/** Breadcrumb, identity and tabs shared by Overview and Job history (BR-04); read-only. */
@Component({
  selector: 'app-technician-profile-header',
  imports: [TranslocoPipe, RouterLink, Tag],
  templateUrl: './technician-profile-header.html',
  styleUrl: './technician-profile-header.scss',
})
export class TechnicianProfileHeader {
  readonly detail = input.required<TechnicianDetail>();
  readonly current = input.required<ProfileTab>();

  readonly statusLabels = PROFILE_STATUS_LABELS;
  readonly statusSeverity = profileStatusSeverity;
  readonly name = computed(() => `${this.detail().firstName} ${this.detail().lastName}`.trim());
  readonly initials = computed(() => initialsOf(this.detail().firstName, this.detail().lastName));
  readonly overviewPath = computed(() => `/team/${this.detail().id}/overview`);
  readonly skillsPath = computed(() => `/team/${this.detail().id}/skills-availability`);
  readonly historyPath = computed(() => `/team/${this.detail().id}/job-history`);
}
