import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Tag } from 'primeng/tag';

import { comingSoonPath } from '../../../../core/config/coming-soon-modules';
import { TechnicianDetail } from '../../models/team.model';
import {
  STATUS_LABELS,
  groupAvailability,
  nextAvailableText,
  proficiencyLabel,
  statusSeverity,
  workloadView,
} from '../../utils/team-format';

export const SKILLS_PATH = comingSoonPath('team-skills');
export const AVAILABILITY_PATH = comingSoonPath('team-availability');
export const SCHEDULE_PATH = '/schedule';

/**
 * BR-13 sections (profile, account link, skills, availability, today) shared by the detail drawer
 * and the Technician's own read-only page. Edit buttons need `canMutate`; the Settings link needs
 * `canViewSettings` (Owner only). Notes are never shown here.
 */
@Component({
  selector: 'app-technician-profile',
  imports: [RouterLink, ButtonDirective, Tag],
  templateUrl: './technician-profile.html',
  styleUrl: './technician-profile.scss',
})
export class TechnicianProfile {
  readonly detail = input.required<TechnicianDetail>();
  readonly canMutate = input(false);
  readonly canViewSettings = input(false);
  /** Disables the account buttons while a request is running. */
  readonly busy = input(false);

  readonly linkRequested = output<void>();
  readonly unlinkRequested = output<void>();

  /** BR-20: "Edit skills" and "Edit availability" open the technician's skills & availability page. */
  readonly editPath = computed(() => `/team/${this.detail().id}/skills-availability`);
  readonly statusLabels = STATUS_LABELS;
  readonly statusSeverity = statusSeverity;

  readonly availability = computed(() => groupAvailability(this.detail().availability));
  readonly skills = computed(() =>
    this.detail().skills.map((skill) => ({
      name: skill.name,
      level: proficiencyLabel(skill.proficiency),
      primary: skill.isPrimary,
    })),
  );
  readonly workload = computed(() =>
    workloadView(this.detail().today.workloadState, this.detail().today.workloadPercent),
  );
  readonly nextAvailable = computed(() =>
    nextAvailableText(this.detail().today.nextAvailable, this.detail().timezone),
  );
}
